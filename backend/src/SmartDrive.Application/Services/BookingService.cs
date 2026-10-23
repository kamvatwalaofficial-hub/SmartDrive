using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Application.Identity;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Application.Services;

/// <summary>
/// Quotes, bookings, simulated payments, cancellations and staff status changes.
/// All prices are calculated here from the database; prices sent by a client are never trusted.
/// </summary>
public sealed class BookingService(
    SmartDriveDbContext db,
    AvailabilityService availability,
    RentalPolicy policy,
    TimeProvider clock,
    AuditWriter audit,
    Notifier notifier,
    ILogger<BookingService> logger)
{
    public const int MaxActiveHoldsPerCustomer = 3;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ================================================================== quotes

    public async Task<QuoteDto> QuoteAsync(QuoteRequest req, CancellationToken ct)
    {
        var (pickup, ret) = ValidatePeriod(req.PickupAt, req.ReturnAt);
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == req.VehicleId && v.ArchivedAt == null, ct)
                      ?? throw DomainException.NotFound("Vehicle");
        var (pickupLocation, returnLocation) = await LocationsAsync(req.PickupLocationId, req.ReturnLocationId, ct);

        var result = await availability.CheckAsync(vehicle, pickup, ret, ct);
        var quote = PricingCalculator.Quote(vehicle.DailyRate, vehicle.DepositAmount, pickup, ret, pickupLocation.City, returnLocation.City, policy);

        return new QuoteDto(
            vehicle.Id, vehicle.DisplayName, result.IsAvailable, result.Reasons.Select(AvailabilityRules.Describe).ToList(),
            quote.RentalDays, quote.DailyRate, quote.Lines.Select(l => l.ToDto()).ToList(),
            quote.Subtotal, quote.Discount, quote.Total, quote.VatIncluded, quote.Deposit,
            Mapping.Local(pickup), Mapping.Local(ret), pickupLocation.Name, returnLocation.Name, Money.Currency);
    }

    // ================================================================== create

    public async Task<BookingDetailDto> CreateAsync(CreateBookingRequest req, Guid customerId, CancellationToken ct)
    {
        var (pickup, ret) = ValidatePeriod(req.PickupAt, req.ReturnAt);
        var customer = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == customerId, ct);
        if (customer is not { IsActive: true })
        {
            throw new DomainException("account.inactive", "Your account is not active.", ErrorKind.Forbidden);
        }

        var now = Now;
        var activeHolds = await db.Bookings.CountAsync(b => b.CustomerId == customerId && b.Status == BookingStatus.Pending && b.HoldExpiresAt > now, ct);
        if (activeHolds >= MaxActiveHoldsPerCustomer)
        {
            throw new DomainException("booking.too_many_holds",
                $"You already have {activeHolds} unpaid bookings. Pay for or cancel one before starting another.", ErrorKind.Conflict);
        }

        // Tracked: the vehicle's concurrency token is bumped in the same save as the new booking.
        var vehicle = await db.Vehicles.Include(v => v.Category).FirstOrDefaultAsync(v => v.Id == req.VehicleId && v.ArchivedAt == null, ct)
                      ?? throw DomainException.NotFound("Vehicle");
        var (pickupLocation, returnLocation) = await LocationsAsync(req.PickupLocationId, req.ReturnLocationId, ct);

        var result = await availability.CheckAsync(vehicle, pickup, ret, ct);
        if (!result.IsAvailable)
        {
            throw new DomainException("booking.unavailable", AvailabilityRules.Describe(result.Reasons[0]), ErrorKind.Conflict);
        }

        var quote = PricingCalculator.Quote(vehicle.DailyRate, vehicle.DepositAmount, pickup, ret, pickupLocation.City, returnLocation.City, policy);
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            Reference = await NewReferenceAsync(ct),
            CustomerId = customerId,
            VehicleId = vehicle.Id,
            PickupLocationId = pickupLocation.Id,
            ReturnLocationId = returnLocation.Id,
            PickupAt = pickup,
            ReturnAt = ret,
            RentalDays = quote.RentalDays,
            DailyRate = quote.DailyRate,
            DiscountAmount = quote.Discount,
            TotalAmount = quote.Total,
            VatAmount = quote.VatIncluded,
            DepositAmount = quote.Deposit,
            Status = BookingStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            HoldExpiresAt = now.AddMinutes(policy.PendingHoldMinutes),
            CreatedAt = now,
            UpdatedAt = now,
        };
        booking.Charges.AddRange(quote.Lines.Select((l, i) => new BookingCharge
        {
            Id = Guid.NewGuid(), Type = l.Type, Description = l.Description, Amount = l.Amount, SortOrder = i,
        }));
        booking.StatusHistory.Add(new BookingStatusChange
        {
            Id = Guid.NewGuid(), ToStatus = BookingStatus.Pending, ChangedByUserId = customerId, ChangedAt = now,
            Reason = $"Created; vehicle held for {policy.PendingHoldMinutes} minutes awaiting payment.",
        });

        db.Bookings.Add(booking);
        vehicle.BookingVersion++;
        audit.Write(customerId, "booking.created", nameof(Booking), booking.Reference, new { booking.VehicleId, booking.TotalAmount });

        await SaveWithConcurrencyGuardAsync(
            "Another customer has just booked this vehicle for overlapping dates. Please check availability and try again.", ct);

        logger.LogInformation("Booking {Reference} created for vehicle {VehicleId}", booking.Reference, booking.VehicleId);
        return await DetailAsync(booking.Reference, customerId, isAdmin: false, ct);
    }

    // ================================================================== simulated payment

    /// <summary>
    /// Records a <em>simulated</em> payment for demonstration. No card data is accepted. The
    /// idempotency key makes retries safe: the same key always returns the original result.
    /// </summary>
    public async Task<BookingDetailDto> PaySimulatedAsync(string reference, Guid userId, string idempotencyKey, bool simulateFailure, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
        {
            throw new DomainException("payment.idempotency_key", "An Idempotency-Key header (up to 100 characters) is required.");
        }

        var booking = await LoadOwnedAsync(reference, userId, ct);
        if (await db.PaymentTransactions.AnyAsync(p => p.IdempotencyKey == idempotencyKey, ct))
        {
            return await DetailAsync(reference, userId, isAdmin: false, ct);
        }

        if (booking.PaymentStatus == PaymentStatus.Paid)
        {
            throw new DomainException("payment.already_paid", "This booking has already been paid.", ErrorKind.Conflict);
        }

        if (booking.Status != BookingStatus.Pending)
        {
            throw new DomainException("payment.not_pending", "Only pending bookings can be paid.", ErrorKind.Conflict);
        }

        var now = Now;
        var vehicle = booking.Vehicle!;

        // The hold may have expired; re-check that nobody else took the vehicle in the meantime.
        var result = await availability.CheckAsync(vehicle, booking.PickupAt, booking.ReturnAt, ct, ignoreBookingId: booking.Id);
        if (!result.IsAvailable)
        {
            Transition(booking, BookingStatus.Cancelled, userId, "Payment hold expired and the vehicle is no longer available.");
            booking.CancellationReason = "Hold expired";
            vehicle.BookingVersion++;
            await db.SaveChangesAsync(ct);
            throw new DomainException("booking.hold_lost",
                "Your reservation hold expired and the vehicle has since been booked. The booking was cancelled and no payment was taken.",
                ErrorKind.Conflict);
        }

        var transaction = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Kind = PaymentKind.Charge,
            Amount = booking.TotalAmount,
            Provider = "Simulated",
            ProviderReference = $"SIM-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            Status = simulateFailure ? PaymentTransactionStatus.Failed : PaymentTransactionStatus.Succeeded,
            IdempotencyKey = idempotencyKey,
            IsSimulated = true,
            CreatedAt = now,
        };
        db.PaymentTransactions.Add(transaction);

        if (simulateFailure)
        {
            audit.Write(userId, "payment.simulated_failure", nameof(Booking), booking.Reference);
            await SaveWithConcurrencyGuardAsync("This booking changed while the payment was being processed. Please refresh and try again.", ct);
            throw new DomainException("payment.declined", "The simulated payment was declined. No money was taken; you can try again.");
        }

        booking.PaymentStatus = PaymentStatus.Paid;
        booking.HoldExpiresAt = null;
        Transition(booking, BookingStatus.Confirmed, userId, "Simulated payment received.");
        vehicle.BookingVersion++; // a second, concurrent payment for this booking will now fail
        audit.Write(userId, "payment.simulated", nameof(Booking), booking.Reference, new { transaction.Amount, transaction.ProviderReference });
        notifier.Notify(booking.CustomerId, "Booking confirmed",
            $"Your {vehicle.DisplayName} is booked for {Format(booking.PickupAt)} ({booking.Reference}).", $"/bookings/{booking.Reference}");

        await SaveWithConcurrencyGuardAsync("This booking is already being paid for. Please refresh to see its status.", ct);
        return await DetailAsync(reference, userId, isAdmin: false, ct);
    }

    // ================================================================== cancellation

    public async Task<CancellationPreviewDto> CancellationPreviewAsync(string reference, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var booking = isAdmin ? await LoadAsync(reference, ct) : await LoadOwnedAsync(reference, userId, ct);
        var a = Assess(booking);
        return new CancellationPreviewDto(a.CanCancel, a.Fee, a.Refund, a.Explanation);
    }

    public async Task<BookingDetailDto> CancelAsync(string reference, Guid userId, string? reason, CancellationToken ct)
    {
        var booking = await LoadOwnedAsync(reference, userId, ct);
        var assessment = Assess(booking);
        if (!assessment.CanCancel)
        {
            throw new DomainException("booking.cannot_cancel", assessment.Explanation, ErrorKind.Conflict);
        }

        ApplyCancellation(booking, userId, reason, assessment.Fee, assessment.Refund, "Cancelled by customer");
        await SaveWithConcurrencyGuardAsync("This booking changed while it was being cancelled. Please refresh and try again.", ct);
        return await DetailAsync(reference, userId, isAdmin: false, ct);
    }

    // ================================================================== reading

    public async Task<IReadOnlyList<BookingSummaryDto>> MineAsync(Guid userId, string scope, CancellationToken ct)
    {
        var now = Now;
        var bookings = await BaseQuery().Where(b => b.CustomerId == userId).OrderBy(b => b.PickupAt).ToListAsync(ct);

        bool IsExpiredHold(Booking b) => b.Status == BookingStatus.Pending && (b.HoldExpiresAt ?? DateTime.MinValue) <= now;

        var filtered = scope switch
        {
            "past" => bookings.Where(b => b.Status == BookingStatus.Completed).OrderByDescending(b => b.ReturnAt),
            "cancelled" => bookings.Where(b => b.Status is BookingStatus.Cancelled or BookingStatus.Rejected || IsExpiredHold(b)).OrderByDescending(b => b.CreatedAt),
            "all" => bookings.OrderByDescending(b => b.PickupAt),
            _ => bookings.Where(b => b.Status is BookingStatus.Confirmed or BookingStatus.Active || (b.Status == BookingStatus.Pending && !IsExpiredHold(b))),
        };

        return filtered.Select(b => Summary(b, includeCustomer: null)).ToList();
    }

    public async Task<BookingDetailDto> DetailAsync(string reference, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var booking = await BaseQuery()
            .Include(b => b.Charges)
            .Include(b => b.StatusHistory)
            .Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Reference == reference, ct);

        // Another customer's booking is reported as "not found" so references cannot be probed.
        if (booking is null || (!isAdmin && booking.CustomerId != userId))
        {
            throw DomainException.NotFound("Booking");
        }

        AppUser? customer = isAdmin ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == booking.CustomerId, ct) : null;
        var assessment = Assess(booking);

        return new BookingDetailDto(
            Summary(booking, customer),
            booking.DailyRate,
            booking.Charges.OrderBy(c => c.SortOrder).Select(c => c.ToDto()).ToList(),
            booking.DiscountAmount,
            booking.VatAmount,
            booking.DepositAmount,
            booking.CancellationFee,
            booking.CancellationReason,
            booking.PickupOdometerKm,
            booking.ReturnOdometerKm,
            booking.StatusHistory.OrderBy(h => h.ChangedAt)
                .Select(h => new StatusChangeDto(h.FromStatus?.ToString(), h.ToStatus.ToString(), Mapping.Local(h.ChangedAt), h.Reason)).ToList(),
            booking.Payments.OrderBy(p => p.CreatedAt)
                .Select(p => new PaymentDto(p.Id, p.Kind.ToString(), p.Amount, p.Currency, p.Provider, p.ProviderReference, p.Status.ToString(), p.IsSimulated, Mapping.Local(p.CreatedAt))).ToList(),
            new CancellationPreviewDto(assessment.CanCancel, assessment.Fee, assessment.Refund, assessment.Explanation),
            isAdmin ? BookingStateMachine.NextStatuses(booking.Status).Select(s => s.ToString()).ToList() : []);
    }

    // ================================================================== administration

    public async Task<PagedResult<BookingSummaryDto>> AdminListAsync(AdminBookingQuery q, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalise(q.Page, q.PageSize);
        var query = FilteredAdminQuery(q);
        var total = await query.CountAsync(ct);
        var bookings = await query.OrderByDescending(b => b.PickupAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var customers = await CustomersAsync(bookings.Select(b => b.CustomerId), ct);
        return new PagedResult<BookingSummaryDto>(
            bookings.Select(b => Summary(b, customers.GetValueOrDefault(b.CustomerId))).ToList(), page, pageSize, total);
    }

    /// <summary>CSV export of the filtered bookings (no payment or personal identity numbers included).</summary>
    public async Task<string> AdminExportCsvAsync(AdminBookingQuery q, CancellationToken ct)
    {
        var bookings = await FilteredAdminQuery(q).OrderByDescending(b => b.PickupAt).Take(5000).ToListAsync(ct);
        var customers = await CustomersAsync(bookings.Select(b => b.CustomerId), ct);
        var csv = new StringBuilder();
        csv.AppendLine("Reference,Status,PaymentStatus,Customer,Vehicle,Category,PickupLocal,ReturnLocal,PickupBranch,ReturnBranch,RentalDays,DailyRate,Discount,Total,CancellationFee,CreatedLocal,SampleData");
        foreach (var b in bookings)
        {
            var c = customers.GetValueOrDefault(b.CustomerId);
            csv.AppendLine(string.Join(",", new string[]
            {
                Csv(b.Reference), Csv(b.Status.ToString()), Csv(b.PaymentStatus.ToString()), Csv(c?.FullName ?? ""),
                Csv(b.Vehicle?.DisplayName ?? ""), Csv(b.Vehicle?.Category?.Name ?? ""),
                Csv(Mapping.Local(b.PickupAt).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                Csv(Mapping.Local(b.ReturnAt).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                Csv(b.PickupLocation?.Name ?? ""), Csv(b.ReturnLocation?.Name ?? ""),
                b.RentalDays.ToString(CultureInfo.InvariantCulture),
                b.DailyRate.ToString("0.00", CultureInfo.InvariantCulture),
                b.DiscountAmount.ToString("0.00", CultureInfo.InvariantCulture),
                b.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture),
                b.CancellationFee.ToString("0.00", CultureInfo.InvariantCulture),
                Csv(Mapping.Local(b.CreatedAt).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                b.IsSampleData ? "yes" : "no",
            }));
        }

        return csv.ToString();
    }

    /// <summary>Staff status changes: confirm, reject, hand over (active), return (completed) or cancel.</summary>
    public async Task<BookingDetailDto> ChangeStatusAsync(string reference, ChangeBookingStatusRequest req, Guid adminId, CancellationToken ct)
    {
        var booking = await LoadAsync(reference, ct);
        var target = Parse.Enum<BookingStatus>(req.Status, "booking status");
        BookingStateMachine.EnsureCanTransition(booking.Status, target);
        var vehicle = booking.Vehicle!;
        var now = Now;

        switch (target)
        {
            case BookingStatus.Confirmed:
            {
                var result = await availability.CheckAsync(vehicle, booking.PickupAt, booking.ReturnAt, ct, ignoreBookingId: booking.Id);
                if (!result.IsAvailable)
                {
                    throw new DomainException("booking.unavailable", "This booking can no longer be confirmed: " + AvailabilityRules.Describe(result.Reasons[0]), ErrorKind.Conflict);
                }

                booking.HoldExpiresAt = null;
                break;
            }

            case BookingStatus.Active:
            {
                if (now < booking.PickupAt.AddHours(-12))
                {
                    throw new DomainException("booking.too_early", "A rental can be started at most 12 hours before the booked pickup time.", ErrorKind.Conflict);
                }

                if (req.OdometerKm is { } km)
                {
                    RecordOdometer(vehicle, booking, km, MileageSource.Pickup, now);
                    booking.PickupOdometerKm = km;
                }

                break;
            }

            case BookingStatus.Completed:
            {
                if (req.OdometerKm is { } km)
                {
                    if (booking.PickupOdometerKm is { } start && km < start)
                    {
                        throw new DomainException("booking.odometer", "The return odometer reading cannot be lower than the pickup reading.");
                    }

                    RecordOdometer(vehicle, booking, km, MileageSource.Return, now);
                    booking.ReturnOdometerKm = km;
                    ApplyExcessMileage(booking, vehicle);
                }

                break;
            }

            case BookingStatus.Cancelled:
            case BookingStatus.Rejected:
            {
                // Business-initiated: no fee, full refund of anything paid.
                var paid = booking.PaymentStatus == PaymentStatus.Paid ? booking.TotalAmount : 0m;
                ApplyCancellation(booking, adminId, req.Reason, 0m, paid, target == BookingStatus.Rejected ? "Rejected by SmartDrive" : "Cancelled by SmartDrive", target);
                await SaveWithConcurrencyGuardAsync("This booking changed while it was being updated. Please refresh and try again.", ct);
                return await DetailAsync(reference, adminId, isAdmin: true, ct);
            }
        }

        Transition(booking, target, adminId, req.Reason);
        vehicle.BookingVersion++;
        audit.Write(adminId, $"booking.{target.ToString().ToLowerInvariant()}", nameof(Booking), booking.Reference, new { req.Reason, req.OdometerKm });
        notifier.Notify(booking.CustomerId, $"Booking {target.ToString().ToLowerInvariant()}",
            $"Your booking {booking.Reference} is now {target.ToString().ToLowerInvariant()}.", $"/bookings/{booking.Reference}");

        await SaveWithConcurrencyGuardAsync("This booking changed while it was being updated. Please refresh and try again.", ct);
        return await DetailAsync(reference, adminId, isAdmin: true, ct);
    }

    // ================================================================== internals

    private (DateTime Pickup, DateTime Return) ValidatePeriod(DateTimeOffset pickupAt, DateTimeOffset returnAt)
    {
        var pickup = pickupAt.UtcDateTime;
        var ret = returnAt.UtcDateTime;
        var problems = RentalPeriod.Validate(pickup, ret, Now, policy);
        if (problems.Count > 0)
        {
            throw new DomainException("rental.invalid_period", string.Join(" ", problems));
        }

        return (pickup, ret);
    }

    private async Task<(Location Pickup, Location Return)> LocationsAsync(int pickupId, int returnId, CancellationToken ct)
    {
        var locations = await db.Locations.AsNoTracking().Where(l => l.IsActive && (l.Id == pickupId || l.Id == returnId)).ToListAsync(ct);
        var pickup = locations.FirstOrDefault(l => l.Id == pickupId) ?? throw new DomainException("location.invalid", "Please choose a valid pickup branch.");
        var ret = locations.FirstOrDefault(l => l.Id == returnId) ?? throw new DomainException("location.invalid", "Please choose a valid return branch.");
        return (pickup, ret);
    }

    private async Task<string> NewReferenceAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var reference = BookingReference.Generate(Random.Shared);
            if (!await db.Bookings.AnyAsync(b => b.Reference == reference, ct))
            {
                return reference;
            }
        }

        throw new InvalidOperationException("Could not generate a unique booking reference.");
    }

    private IQueryable<Booking> BaseQuery() =>
        db.Bookings
            .Include(b => b.Vehicle).ThenInclude(v => v!.Category)
            .Include(b => b.Vehicle).ThenInclude(v => v!.Media)
            .Include(b => b.PickupLocation)
            .Include(b => b.ReturnLocation)
            .Include(b => b.Review)
            .AsSplitQuery();

    private IQueryable<Booking> FilteredAdminQuery(AdminBookingQuery q)
    {
        var query = BaseQuery().AsNoTracking();
        if (Parse.OptionalEnum<BookingStatus>(q.Status, "booking status") is { } status)
        {
            query = query.Where(b => b.Status == status);
        }

        if (q.VehicleId is { } vehicleId)
        {
            query = query.Where(b => b.VehicleId == vehicleId);
        }

        if (q.From is { } from)
        {
            var f = from.UtcDateTime;
            query = query.Where(b => b.ReturnAt >= f);
        }

        if (q.To is { } to)
        {
            var t = to.UtcDateTime;
            query = query.Where(b => b.PickupAt <= t);
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim().ToUpperInvariant();
            var customerIds = db.Users.Where(u => u.Email!.ToUpper().Contains(term) || (u.FirstName + " " + u.LastName).ToUpper().Contains(term)).Select(u => u.Id);
            query = query.Where(b => b.Reference.Contains(term) || b.Vehicle!.Make.ToUpper().Contains(term) || b.Vehicle!.Model.ToUpper().Contains(term) || customerIds.Contains(b.CustomerId));
        }

        return query;
    }

    private async Task<Dictionary<Guid, AppUser>> CustomersAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
    }

    private async Task<Booking> LoadAsync(string reference, CancellationToken ct) =>
        await db.Bookings.Include(b => b.Vehicle).Include(b => b.Payments).FirstOrDefaultAsync(b => b.Reference == reference, ct)
        ?? throw DomainException.NotFound("Booking");

    private async Task<Booking> LoadOwnedAsync(string reference, Guid userId, CancellationToken ct)
    {
        var booking = await LoadAsync(reference, ct);
        return booking.CustomerId == userId ? booking : throw DomainException.NotFound("Booking");
    }

    private CancellationAssessment Assess(Booking b) =>
        CancellationPolicy.Assess(b.Status, b.PaymentStatus, b.PickupAt, b.DailyRate, b.TotalAmount, Now, policy);

    private void ApplyCancellation(Booking booking, Guid actorId, string? reason, decimal fee, decimal refund, string historyNote, BookingStatus target = BookingStatus.Cancelled)
    {
        var now = Now;
        Transition(booking, target, actorId, string.IsNullOrWhiteSpace(reason) ? historyNote : $"{historyNote}: {reason}");
        booking.CancellationFee = fee;
        booking.CancellationReason = reason;
        booking.HoldExpiresAt = null;

        if (refund > 0)
        {
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                Kind = PaymentKind.Refund,
                Amount = refund,
                Provider = "Simulated",
                ProviderReference = $"SIMR-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                Status = PaymentTransactionStatus.Succeeded,
                IdempotencyKey = $"refund-{booking.Id:N}",
                IsSimulated = true,
                CreatedAt = now,
            });
            booking.PaymentStatus = fee > 0 ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;
        }

        if (fee > 0)
        {
            booking.Charges.Add(new BookingCharge
            {
                Id = Guid.NewGuid(), Type = ChargeType.CancellationFee, Description = "Late cancellation fee (retained)", Amount = fee, SortOrder = 90,
            });
        }

        booking.Vehicle!.BookingVersion++;
        audit.Write(actorId, $"booking.{target.ToString().ToLowerInvariant()}", nameof(Booking), booking.Reference, new { fee, refund });
        notifier.Notify(booking.CustomerId, target == BookingStatus.Rejected ? "Booking rejected" : "Booking cancelled",
            refund > 0 ? $"{booking.Reference} was cancelled. A simulated refund of {Money.Format(refund)} has been recorded."
                       : $"{booking.Reference} was cancelled.",
            $"/bookings/{booking.Reference}");
    }

    private void Transition(Booking booking, BookingStatus target, Guid actorId, string? reason)
    {
        BookingStateMachine.EnsureCanTransition(booking.Status, target);
        var now = Now;
        booking.StatusHistory.Add(new BookingStatusChange
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, FromStatus = booking.Status, ToStatus = target,
            ChangedByUserId = actorId, Reason = reason, ChangedAt = now,
        });
        booking.Status = target;
        booking.UpdatedAt = now;
    }

    private void RecordOdometer(Vehicle vehicle, Booking booking, int km, MileageSource source, DateTime now)
    {
        if (km < vehicle.OdometerKm)
        {
            throw new DomainException("vehicle.odometer", $"The odometer reading cannot be lower than the last recorded reading ({vehicle.OdometerKm:N0} km).");
        }

        vehicle.OdometerKm = km;
        vehicle.UpdatedAt = now;
        db.MileageRecords.Add(new MileageRecord { Id = Guid.NewGuid(), VehicleId = vehicle.Id, BookingId = booking.Id, OdometerKm = km, Source = source, RecordedAt = now });
    }

    private static void ApplyExcessMileage(Booking booking, Vehicle vehicle)
    {
        if (vehicle.DailyMileageLimitKm is not { } limit || vehicle.ExcessMileageRatePerKm is not { } rate
            || booking.PickupOdometerKm is not { } start || booking.ReturnOdometerKm is not { } end)
        {
            return;
        }

        var allowed = limit * booking.RentalDays;
        var excess = end - start - allowed;
        if (excess <= 0)
        {
            return;
        }

        var amount = Money.Round(excess * rate);
        booking.Charges.Add(new BookingCharge
        {
            Id = Guid.NewGuid(), Type = ChargeType.ExcessMileage,
            Description = $"Excess mileage: {excess:N0} km over {allowed:N0} km × {Money.Format(rate)}", Amount = amount, SortOrder = 80,
        });
        booking.TotalAmount = Money.Round(booking.TotalAmount + amount);
    }

    private async Task SaveWithConcurrencyGuardAsync(string conflictMessage, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Concurrency conflict while saving a booking change");
            throw new DomainException("booking.concurrent_change", conflictMessage, ErrorKind.Conflict);
        }
    }

    private BookingSummaryDto Summary(Booking b, AppUser? includeCustomer)
    {
        var now = Now;
        var expired = b.Status == BookingStatus.Pending && (b.HoldExpiresAt ?? DateTime.MinValue) <= now;
        var assessment = Assess(b);
        var canReview = b.Review is null && ReviewEligibility.Check(b.CustomerId, b.Status, b.ReturnAt, false, b.CustomerId, now, policy) is null;
        var image = b.Vehicle?.Media.PrimaryImage();

        return new BookingSummaryDto(
            b.Id,
            b.Reference,
            b.VehicleId,
            b.Vehicle?.DisplayName ?? "",
            image?.Url,
            Mapping.Local(b.PickupAt),
            Mapping.Local(b.ReturnAt),
            b.PickupLocation?.Name ?? "",
            b.ReturnLocation?.Name ?? "",
            b.RentalDays,
            b.TotalAmount,
            expired ? "Expired" : b.Status.ToString(),
            b.PaymentStatus.ToString(),
            expired ? null : Mapping.Local(b.HoldExpiresAt),
            assessment.CanCancel && !expired,
            b.Status == BookingStatus.Pending && !expired && b.PaymentStatus == PaymentStatus.Unpaid,
            canReview,
            b.Review is not null && b.Review.Status != ReviewStatus.Removed,
            Mapping.Local(b.CreatedAt),
            includeCustomer?.FullName,
            includeCustomer?.Email,
            b.IsSampleData);
    }

    private static string Format(DateTime utc) => Mapping.Local(utc).ToString("ddd d MMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 || value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@')
            ? "\"" + (value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@') ? "'" : "") + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
