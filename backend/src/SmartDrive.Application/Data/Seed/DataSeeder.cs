using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartDrive.Application.Identity;
using SmartDrive.Domain.Analytics;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Application.Data.Seed;

/// <summary>Settings for the development/demo seed, bound from the "Seed" configuration section.</summary>
public sealed class SeedOptions
{
    /// <summary>Creates roles, demo accounts and sample data on startup when true.</summary>
    public bool Enabled { get; set; }

    /// <summary>Generates the sample fleet, bookings and reviews (roles and demo accounts are always ensured).</summary>
    public bool SampleData { get; set; } = true;

    public string AdminEmail { get; set; } = "admin@smartdrive.example";
    public string? AdminPassword { get; set; }
    public string CustomerEmail { get; set; } = "customer@smartdrive.example";
    public string? CustomerPassword { get; set; }

    /// <summary>Months of booking history to generate.</summary>
    public int HistoryMonths { get; set; } = 18;

    /// <summary>Fixed seed for the random generator, so every developer gets the same data.</summary>
    public int RandomSeed { get; set; } = 2026;
}

/// <summary>
/// Creates the roles, two demo accounts and a realistic sample business: a fleet, branches,
/// customers and about 18 months of bookings, payments, maintenance, mileage and reviews.
///
/// History is generated vehicle by vehicle in date order, so the data obeys the same rules as
/// real data: no overlapping rentals, a turnaround gap between rentals, services every
/// <see cref="Vehicle.ServiceIntervalKm"/> with downtime, and prices from <see cref="PricingCalculator"/>.
/// Every generated record is flagged as sample data. Running it again does nothing once data exists.
/// </summary>
public sealed class DataSeeder(
    SmartDriveDbContext db,
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    RentalPolicy policy,
    TimeProvider clock,
    SeedOptions options,
    ILogger<DataSeeder> logger)
{
    private const string MediaAttribution = "Original illustration (sample media)";

    private Random rng = new(2026);
    private DateTime now;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        rng = new Random(options.RandomSeed);
        now = TruncateToMinute(clock.GetUtcNow().UtcDateTime);

        await EnsureRolesAsync();
        var admin = await EnsureAccountAsync(options.AdminEmail, options.AdminPassword, "Amara", "Admin", Roles.Administrator, ct);
        var demoCustomer = await EnsureAccountAsync(options.CustomerEmail, options.CustomerPassword, "Demo", "Customer", Roles.Customer, ct);

        if (!options.SampleData || await db.Vehicles.AnyAsync(ct))
        {
            return;
        }

        logger.LogInformation("Seeding sample data ({Months} months of history)", options.HistoryMonths);

        var categories = SampleData.Categories
            .Select(c => new VehicleCategory { Name = c.Name, Slug = c.Slug, Description = c.Description, SortOrder = c.SortOrder })
            .ToDictionary(c => c.Slug);
        db.VehicleCategories.AddRange(categories.Values);

        var locations = SampleData.Locations
            .Select(l => new Location { Name = l.Name, Code = l.Code, AddressLine = l.AddressLine, City = l.City, Province = l.Province, IsAirport = l.IsAirport })
            .ToDictionary(l => l.Code);
        db.Locations.AddRange(locations.Values);

        var customers = CreateSampleCustomers();
        var customerPool = customers.Select(c => c.Id).ToList();

        var start = now.AddMonths(-options.HistoryMonths);
        var plans = new List<VehiclePlan>();
        for (var i = 0; i < SampleData.Vehicles.Length; i++)
        {
            var seed = SampleData.Vehicles[i];
            var vehicle = CreateVehicle(seed, categories[seed.Category], start, i);
            plans.Add(new VehiclePlan(seed, vehicle, locations[seed.HomeLocationCode], i));
        }

        db.Vehicles.AddRange(plans.Select(p => p.Vehicle));

        // Fixed situations that make the dashboard interesting (and show the rules working):
        //   one bakkie is in the workshop right now, one SUV has a service booked next week.
        var workshop = plans.Single(p => p.Seed.Slug == "toyota-hilux");
        AddFixedMaintenance(workshop, MaintenanceType.Repair, MaintenanceStatus.InProgress, "Clutch replacement",
            "Clutch slipping under load. Clutch kit and flywheel being replaced.", now.AddDays(-1), now.AddDays(2), isIssue: true);
        var scheduled = plans.Single(p => p.Seed.Slug == "haval-jolion");
        AddFixedMaintenance(scheduled, MaintenanceType.Service, MaintenanceStatus.Scheduled, "Scheduled 30 000 km service",
            "Routine service booked in advance.", now.AddDays(8).Date.AddHours(6), now.AddDays(9).Date.AddHours(6), isIssue: false);

        var allBookings = new List<Booking>();
        foreach (var plan in plans)
        {
            allBookings.AddRange(GenerateHistory(plan, start, customerPool, locations));
        }

        // A recognisable history for the demo customer account.
        if (demoCustomer is not null)
        {
            AssignDemoCustomerBookings(allBookings, demoCustomer.Id);
        }

        db.Bookings.AddRange(allBookings);
        AddReviews(allBookings, plans, customers, demoCustomer?.Id);
        ApplyFleetHealthScenarios(plans);

        foreach (var plan in plans)
        {
            plan.Vehicle.Media.AddRange(CreateMedia(plan.Seed));
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = admin?.Id,
            Action = "seed.sample_data",
            EntityType = "System",
            EntityId = "seed",
            Details = $"{{\"vehicles\":{plans.Count},\"bookings\":{allBookings.Count}}}",
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Sample data seeded: {Vehicles} vehicles, {Bookings} bookings, {Customers} customers",
            plans.Count, allBookings.Count, customers.Count);
    }

    // ------------------------------------------------------------------ roles and accounts

    private async Task EnsureRolesAsync()
    {
        foreach (var role in new[] { Roles.Customer, Roles.Administrator })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(role));
            }
        }
    }

    private async Task<AppUser?> EnsureAccountAsync(string email, string? password, string first, string last, string role, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("No password configured for demo account {Email}; it was not created", email);
                return null;
            }

            user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = first,
                LastName = last,
                PhoneNumber = "+27 00 000 0000",
                CreatedAt = now,
                IsSampleData = true,
            };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create demo account {email}: {string.Join("; ", created.Errors.Select(e => e.Description))}");
            }

            db.CustomerProfiles.Add(new CustomerProfile { UserId = user.Id, City = "Gqeberha", Province = "Eastern Cape", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(ct);
        }

        if (!await users.IsInRoleAsync(user, role))
        {
            await users.AddToRoleAsync(user, role);
        }

        return user;
    }

    /// <summary>Sample customers have no password, so nobody can sign in as them.</summary>
    private List<AppUser> CreateSampleCustomers()
    {
        var customerRoleId = db.Roles.AsNoTracking().Where(r => r.Name == Roles.Customer).Select(r => r.Id).First();
        var result = new List<AppUser>();
        foreach (var (first, last, city, province) in SampleData.Customers)
        {
            var email = $"{Slug(first)}.{Slug(last)}@example.com";
            var created = now.AddMonths(-options.HistoryMonths).AddDays(-rng.Next(5, 200));
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                FirstName = first,
                LastName = last,
                PhoneNumber = $"+27 8{rng.Next(1, 4)} {rng.Next(100, 999)} {rng.Next(1000, 9999)}",
                CreatedAt = created,
                LastLoginAt = null,
                IsSampleData = true,
                LockoutEnabled = true,
            };
            db.Users.Add(user);
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = customerRoleId });
            db.CustomerProfiles.Add(new CustomerProfile { UserId = user.Id, City = city, Province = province, CreatedAt = created, UpdatedAt = created });
            result.Add(user);
        }

        return result;
    }

    // ------------------------------------------------------------------ fleet

    private sealed class VehiclePlan(SampleData.VehicleSeed seed, Vehicle vehicle, Location home, int index)
    {
        public SampleData.VehicleSeed Seed { get; } = seed;
        public Vehicle Vehicle { get; } = vehicle;
        public Location Home { get; } = home;
        public int Index { get; } = index;
        public List<(DateTime Start, DateTime End)> Blocked { get; } = [];
        public DateTime? LastServiceAt { get; set; }
        public int LastServiceKm { get; set; }
    }

    private Vehicle CreateVehicle(SampleData.VehicleSeed s, VehicleCategory category, DateTime historyStart, int index)
    {
        // A few vehicles joined the fleet part-way through the history.
        var joined = index % 7 == 6 ? historyStart.AddMonths(6 + rng.Next(0, 4)) : historyStart.AddDays(-rng.Next(10, 60));
        var startKm = (now.Year - s.Year) * 9000 + rng.Next(1500, 6000);
        return new Vehicle
        {
            Id = Guid.NewGuid(),
            RegistrationNumber = s.Registration,
            Make = s.Make,
            Model = s.Model,
            Year = s.Year,
            Category = category,
            Description = s.Description,
            Seats = s.Seats,
            Doors = s.Doors,
            LuggageCapacity = s.Luggage,
            Transmission = s.Transmission,
            FuelType = s.Fuel,
            FuelEconomyLitresPer100Km = s.LitresPer100Km,
            ComfortRating = s.Comfort,
            Features = [.. s.Features],
            DailyRate = s.DailyRate,
            DailyMileageLimitKm = s.MileageLimitKm,
            ExcessMileageRatePerKm = s.ExcessPerKm,
            DepositAmount = s.Deposit,
            Status = VehicleStatus.Available,
            OdometerKm = Math.Max(500, startKm),
            ServiceIntervalKm = 15000,
            IsSampleData = true,
            CreatedAt = joined,
            UpdatedAt = joined,
        };
    }

    private void AddFixedMaintenance(VehiclePlan plan, MaintenanceType type, MaintenanceStatus status, string title, string description,
        DateTime startAt, DateTime endAt, bool isIssue)
    {
        plan.Vehicle.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = Guid.NewGuid(),
            Type = type,
            Status = status,
            Title = title,
            Description = description,
            StartAt = startAt,
            EndAt = endAt,
            IsReportedIssue = isIssue,
            IsSampleData = true,
            CreatedAt = startAt.AddDays(-3) < now ? startAt.AddDays(-3) : now,
            UpdatedAt = now,
        });
        plan.Blocked.Add((startAt, endAt + policy.Turnaround));
    }

    // ------------------------------------------------------------------ bookings

    private IEnumerable<Booking> GenerateHistory(VehiclePlan plan, DateTime historyStart, List<Guid> customers, Dictionary<string, Location> locations)
    {
        var vehicle = plan.Vehicle;
        var bookings = new List<Booking>();
        var cursor = Max(historyStart, vehicle.CreatedAt).AddDays(rng.Next(0, 6));
        var horizon = now.AddDays(45);
        var otherCities = locations.Values.Where(l => l.City != plan.Home.City).ToList();
        plan.LastServiceKm = vehicle.OdometerKm - rng.Next(0, 8000);
        plan.LastServiceAt = cursor.AddDays(-rng.Next(10, 120));

        while (cursor < horizon)
        {
            // Demand: the vehicle's base popularity, scaled by the season, sets the idle gap.
            var demand = plan.Seed.Demand * SampleData.Season(SouthAfricanTime.ToLocal(cursor).Month);
            var gapDays = Math.Max(0, (int)Math.Round(Exponential(2.6 / Math.Max(0.15, demand))));
            var pickupLocal = SouthAfricanTime.ToLocal(cursor).Date.AddDays(gapDays).AddHours(rng.Next(7, 13)).AddMinutes(rng.Next(0, 4) * 15);
            var pickup = DateTime.SpecifyKind(pickupLocal - SouthAfricanTime.Offset, DateTimeKind.Utc);
            while (pickup < cursor)
            {
                // A same-day slot can fall before the vehicle is back and prepared; move to the next day.
                pickup = pickup.AddDays(1);
            }

            var days = RentalLength(plan.Seed.Category);
            var ret = pickup.AddDays(days).AddHours(rng.Next(-2, 3));
            if (ret <= pickup.AddHours(4))
            {
                ret = pickup.AddDays(1);
            }

            var clash = plan.Blocked.FirstOrDefault(b => pickup < b.End && b.Start < ret + policy.Turnaround);
            if (clash != default)
            {
                cursor = clash.End;
                continue;
            }

            if (pickup >= horizon)
            {
                break;
            }

            var oneWay = rng.NextDouble() < 0.12 && otherCities.Count > 0;
            var returnLocation = oneWay ? otherCities[rng.Next(otherCities.Count)] : plan.Home;
            var booking = BuildBooking(plan, customers[rng.Next(customers.Count)], pickup, ret, plan.Home, returnLocation);
            bookings.Add(booking);

            cursor = ret + policy.Turnaround;

            // A completed rental moves the odometer and may trigger a service or a reported fault.
            if (booking.Status == BookingStatus.Completed && booking.ReturnOdometerKm is { } km)
            {
                if (km - plan.LastServiceKm >= vehicle.ServiceIntervalKm || (ret - plan.LastServiceAt!.Value).TotalDays > 365)
                {
                    cursor = AddRoutineService(plan, cursor, km);
                }
                else if (rng.NextDouble() < 0.03)
                {
                    cursor = AddReportedIssue(plan, cursor, km);
                }
            }
        }

        // Service schedule for the future, from the last recorded service.
        vehicle.NextServiceDueKm = plan.LastServiceKm + vehicle.ServiceIntervalKm;
        vehicle.NextServiceDueDate = plan.LastServiceAt?.AddDays(365);
        return bookings;
    }

    private int RentalLength(string category)
    {
        var r = rng.NextDouble();
        var days = r switch
        {
            < 0.30 => rng.Next(1, 3),
            < 0.70 => rng.Next(3, 6),
            < 0.90 => rng.Next(6, 10),
            < 0.97 => rng.Next(10, 16),
            _ => rng.Next(21, 29),
        };
        return category is "minivan" or "suv" ? days + rng.Next(0, 2) : days;
    }

    private Booking BuildBooking(VehiclePlan plan, Guid customerId, DateTime pickup, DateTime ret, Location from, Location to)
    {
        var v = plan.Vehicle;
        var quote = PricingCalculator.Quote(v.DailyRate, v.DepositAmount, pickup, ret, from.City, to.City, policy);
        var created = Min(pickup.AddDays(-rng.Next(1, 35)).AddHours(-rng.Next(0, 12)), now.AddMinutes(-rng.Next(30, 600)));
        if (created < v.CreatedAt)
        {
            created = v.CreatedAt;
        }

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            Reference = NewReference(),
            CustomerId = customerId,
            VehicleId = v.Id,
            Vehicle = v,
            PickupLocation = from,
            ReturnLocation = to,
            PickupAt = pickup,
            ReturnAt = ret,
            RentalDays = quote.RentalDays,
            DailyRate = quote.DailyRate,
            DiscountAmount = quote.Discount,
            TotalAmount = quote.Total,
            VatAmount = quote.VatIncluded,
            DepositAmount = quote.Deposit,
            IsSampleData = true,
            CreatedAt = created,
            UpdatedAt = created,
        };
        booking.Charges.AddRange(quote.Lines.Select((l, i) => new BookingCharge
        {
            Id = Guid.NewGuid(), Type = l.Type, Description = l.Description, Amount = l.Amount, SortOrder = i,
        }));

        History(booking, null, BookingStatus.Pending, created, "Created (sample data)");
        var paidAt = created.AddMinutes(rng.Next(2, 25));
        Pay(booking, paidAt);
        History(booking, BookingStatus.Pending, BookingStatus.Confirmed, paidAt, "Simulated payment received");

        var isPast = ret < now;
        var isCurrent = pickup <= now && ret >= now;
        var cancel = rng.NextDouble() < (isPast ? 0.08 : 0.05) && pickup > created.AddHours(6);

        if (cancel)
        {
            var cancelledAt = Min(created.AddHours((pickup - created).TotalHours * rng.NextDouble()), pickup.AddHours(-1));
            if (cancelledAt > now)
            {
                cancelledAt = now.AddHours(-rng.Next(1, 48));
            }

            if (cancelledAt <= paidAt)
            {
                cancelledAt = paidAt.AddMinutes(30);
            }

            var assessment = CancellationPolicy.Assess(BookingStatus.Confirmed, PaymentStatus.Paid, pickup, booking.DailyRate, booking.TotalAmount, cancelledAt, policy);
            booking.Status = BookingStatus.Cancelled;
            booking.CancellationFee = assessment.Fee;
            booking.CancellationReason = "Plans changed (sample data)";
            booking.PaymentStatus = assessment.Fee > 0 ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;
            if (assessment.Fee > 0)
            {
                booking.Charges.Add(new BookingCharge
                {
                    Id = Guid.NewGuid(), Type = ChargeType.CancellationFee, Description = "Late cancellation fee", Amount = assessment.Fee, SortOrder = booking.Charges.Count,
                });
            }

            booking.Payments.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(), Kind = PaymentKind.Refund, Amount = assessment.Refund, Status = PaymentTransactionStatus.Succeeded,
                ProviderReference = $"SIM-R-{booking.Reference[3..]}", IdempotencyKey = $"seed-refund-{booking.Id:N}", CreatedAt = cancelledAt,
            });
            History(booking, BookingStatus.Confirmed, BookingStatus.Cancelled, cancelledAt, assessment.Explanation);
            booking.UpdatedAt = cancelledAt;
            return booking;
        }

        if (isPast || isCurrent)
        {
            var pickupKm = plan.Vehicle.OdometerKm;
            booking.PickupOdometerKm = pickupKm;
            History(booking, BookingStatus.Confirmed, BookingStatus.Active, pickup, "Vehicle collected");
            AddMileage(plan.Vehicle, booking.Id, pickupKm, MileageSource.Pickup, pickup);
            booking.Status = BookingStatus.Active;
            booking.UpdatedAt = pickup;

            if (isPast)
            {
                var perDay = plan.Seed.Category is "bakkie" or "suv" or "minivan" ? rng.Next(120, 420) : rng.Next(60, 300);
                var km = Math.Max(15, (int)(perDay * Math.Max(1, (ret - pickup).TotalDays)));
                var returnKm = pickupKm + km;
                booking.ReturnOdometerKm = returnKm;
                plan.Vehicle.OdometerKm = returnKm;
                AddMileage(plan.Vehicle, booking.Id, returnKm, MileageSource.Return, ret);

                if (plan.Vehicle.DailyMileageLimitKm is { } limit && plan.Vehicle.ExcessMileageRatePerKm is { } rate)
                {
                    var excess = km - limit * booking.RentalDays;
                    if (excess > 0)
                    {
                        var charge = Money.Round(excess * rate);
                        booking.Charges.Add(new BookingCharge
                        {
                            Id = Guid.NewGuid(), Type = ChargeType.ExcessMileage, Description = $"Excess mileage: {excess} km × {Money.Format(rate)}",
                            Amount = charge, SortOrder = booking.Charges.Count,
                        });
                        booking.TotalAmount = Money.Round(booking.TotalAmount + charge);
                        booking.VatAmount = Money.Round(booking.TotalAmount * policy.VatRate / (1 + policy.VatRate));
                        booking.Payments.Add(new PaymentTransaction
                        {
                            Id = Guid.NewGuid(), Kind = PaymentKind.Charge, Amount = charge, Status = PaymentTransactionStatus.Succeeded,
                            ProviderReference = $"SIM-X-{booking.Reference[3..]}", IdempotencyKey = $"seed-excess-{booking.Id:N}", CreatedAt = ret,
                        });
                    }
                }

                booking.Status = BookingStatus.Completed;
                booking.UpdatedAt = ret;
                History(booking, BookingStatus.Active, BookingStatus.Completed, ret, "Vehicle returned");
            }
        }
        else
        {
            booking.Status = BookingStatus.Confirmed;
            booking.UpdatedAt = paidAt;
        }

        return booking;
    }

    private void Pay(Booking booking, DateTime at)
    {
        booking.PaymentStatus = PaymentStatus.Paid;
        booking.Payments.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            Kind = PaymentKind.Charge,
            Amount = booking.TotalAmount,
            Status = PaymentTransactionStatus.Succeeded,
            ProviderReference = $"SIM-{booking.Reference[3..]}",
            IdempotencyKey = $"seed-{booking.Id:N}",
            CreatedAt = at,
        });
    }

    private static void History(Booking booking, BookingStatus? from, BookingStatus to, DateTime at, string reason) =>
        booking.StatusHistory.Add(new BookingStatusChange { Id = Guid.NewGuid(), FromStatus = from, ToStatus = to, ChangedAt = at, Reason = reason });

    private void AddMileage(Vehicle vehicle, Guid? bookingId, int km, MileageSource source, DateTime at) =>
        db.MileageRecords.Add(new MileageRecord { Id = Guid.NewGuid(), VehicleId = vehicle.Id, BookingId = bookingId, OdometerKm = km, Source = source, RecordedAt = at });

    private DateTime AddRoutineService(VehiclePlan plan, DateTime cursor, int km)
    {
        var start = cursor.AddHours(rng.Next(2, 30));
        var end = start.AddDays(1);
        if (end > now)
        {
            return cursor;
        }

        plan.Vehicle.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = Guid.NewGuid(),
            Type = MaintenanceType.Service,
            Status = MaintenanceStatus.Completed,
            Title = $"{RoundKm(km)} km service",
            Description = "Routine service: oil and filters, brake and tyre inspection.",
            StartAt = start,
            EndAt = end,
            OdometerKm = km,
            Cost = Money.Round(1800 + rng.Next(0, 2200) + (plan.Vehicle.DailyRate > 1500 ? 2500 : 0)),
            IsSampleData = true,
            CreatedAt = start,
            UpdatedAt = end,
        });
        AddMileage(plan.Vehicle, null, km, MileageSource.Service, start);
        plan.LastServiceKm = km;
        plan.LastServiceAt = start;
        return end + policy.Turnaround;
    }

    private DateTime AddReportedIssue(VehiclePlan plan, DateTime cursor, int km)
    {
        var (type, title, description) = SampleData.ReportedIssues[rng.Next(SampleData.ReportedIssues.Length)];
        var start = cursor.AddHours(rng.Next(2, 20));
        var end = start.AddDays(rng.Next(1, 3));
        if (end > now)
        {
            return cursor;
        }

        plan.Vehicle.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = Guid.NewGuid(), Type = type, Status = MaintenanceStatus.Completed, Title = title, Description = description,
            StartAt = start, EndAt = end, OdometerKm = km, Cost = Money.Round(600 + rng.Next(0, 3500)),
            IsReportedIssue = true, IsSampleData = true, CreatedAt = start, UpdatedAt = end,
        });
        return end + policy.Turnaround;
    }

    /// <summary>
    /// Health scenarios for the maintenance alerts: one service overdue, one due soon, and one
    /// vehicle with repeated reported faults in the last 90 days.
    /// </summary>
    private void ApplyFleetHealthScenarios(List<VehiclePlan> plans)
    {
        var overdue = plans.Single(p => p.Seed.Slug == "vw-polo-vivo").Vehicle;
        overdue.NextServiceDueKm = overdue.OdometerKm - 850;
        overdue.NextServiceDueDate = now.AddDays(-12);

        var dueSoon = plans.Single(p => p.Seed.Slug == "toyota-fortuner").Vehicle;
        dueSoon.NextServiceDueKm = dueSoon.OdometerKm + 600;

        var repeated = plans.Single(p => p.Seed.Slug == "vw-polo-sedan").Vehicle;
        foreach (var daysAgo in new[] { 70, 25 })
        {
            var (type, title, description) = SampleData.ReportedIssues[daysAgo == 70 ? 1 : 0];
            var start = now.AddDays(-daysAgo).Date.AddHours(7);
            repeated.MaintenanceRecords.Add(new MaintenanceRecord
            {
                Id = Guid.NewGuid(), Type = type, Status = MaintenanceStatus.Completed, Title = title, Description = description,
                StartAt = start, EndAt = start.AddHours(6), Cost = 950m, IsReportedIssue = true,
                IsSampleData = true, CreatedAt = start, UpdatedAt = start.AddHours(6),
            });
        }
    }

    private void AssignDemoCustomerBookings(List<Booking> bookings, Guid demoCustomerId)
    {
        void Take(Func<Booking, bool> predicate, int count)
        {
            foreach (var b in bookings.Where(predicate).OrderByDescending(b => b.PickupAt).Take(count))
            {
                b.CustomerId = demoCustomerId;
            }
        }

        Take(b => b.Status == BookingStatus.Completed && b.ReturnAt > now.AddDays(-40), 2);
        Take(b => b.Status == BookingStatus.Completed && b.ReturnAt < now.AddDays(-60) && b.ReturnAt > now.AddDays(-200), 2);
        Take(b => b.Status == BookingStatus.Confirmed && b.PickupAt > now.AddDays(3), 1);
        Take(b => b.Status == BookingStatus.Cancelled && b.CustomerId != demoCustomerId, 1);
    }

    // ------------------------------------------------------------------ reviews

    private void AddReviews(List<Booking> bookings, List<VehiclePlan> plans, List<AppUser> customers, Guid? demoCustomerId)
    {
        var byVehicle = plans.ToDictionary(p => p.Vehicle.Id);
        var names = customers.ToDictionary(c => c.Id);
        var demoReviewed = false;

        foreach (var booking in bookings.Where(b => b.Status == BookingStatus.Completed && b.ReturnAt > now.AddMonths(-14)).OrderBy(b => b.ReturnAt))
        {
            var isDemo = booking.CustomerId == demoCustomerId;
            if (isDemo)
            {
                // The demo customer has one reviewed rental and leaves the most recent one to review.
                if (demoReviewed || booking.ReturnAt > now.AddDays(-40))
                {
                    continue;
                }

                demoReviewed = true;
            }
            else if (rng.NextDouble() > 0.55 || !names.ContainsKey(booking.CustomerId))
            {
                continue;
            }

            var plan = byVehicle[booking.VehicleId];
            var quality = plan.Seed.Comfort / 5.0 + (rng.NextDouble() - 0.5) * 0.9;
            if (plan.Seed.Slug == "vw-polo-sedan")
            {
                quality -= 0.25; // the vehicle with recurring faults gets weaker reviews
            }

            var overall = Math.Clamp((int)Math.Round(2.2 + quality * 3.0), 1, 5);
            var review = BuildReview(booking, overall);
            booking.Review = review;
        }
    }

    private Review BuildReview(Booking booking, int overall)
    {
        int Near(int value) => Math.Clamp(value + rng.Next(-1, 2), 1, 5);

        var themes = SampleData.ReviewThemes.OrderBy(_ => rng.Next()).Take(rng.Next(2, 4)).ToList();
        var sentences = new List<string>();
        var themeScores = new Dictionary<string, int>();
        foreach (var (theme, good, bad) in themes)
        {
            var positive = overall switch
            {
                >= 5 => rng.NextDouble() < 0.95,
                4 => rng.NextDouble() < 0.8,
                3 => rng.NextDouble() < 0.5,
                _ => rng.NextDouble() < 0.15,
            };
            sentences.Add(positive ? good[rng.Next(good.Length)] : bad[rng.Next(bad.Length)]);
            themeScores[theme] = positive ? Near(Math.Max(overall, 4)) : Near(Math.Min(overall, 2));
        }

        int ScoreFor(string theme) => themeScores.TryGetValue(theme, out var s) ? s : Near(overall);

        var title = overall >= 4 ? SampleData.PositiveTitles[rng.Next(SampleData.PositiveTitles.Length)]
            : overall == 3 ? SampleData.MixedTitles[rng.Next(SampleData.MixedTitles.Length)]
            : SampleData.NegativeTitles[rng.Next(SampleData.NegativeTitles.Length)];
        var comment = string.Join(" ", sentences);
        var createdAt = Min(booking.ReturnAt.AddHours(rng.Next(3, 24 * 10)), now.AddMinutes(-5));
        var sentiment = SentimentAnalyzer.Analyse(title, comment);

        return new Review
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            VehicleId = booking.VehicleId,
            CustomerId = booking.CustomerId,
            Overall = overall,
            Condition = ScoreFor("condition"),
            Cleanliness = ScoreFor("cleanliness"),
            Comfort = ScoreFor("comfort"),
            Value = ScoreFor("value"),
            Title = title,
            Comment = comment,
            Status = ReviewStatus.Published,
            SentimentLabel = sentiment.Label,
            SentimentScore = sentiment.Score,
            PositiveThemes = [.. sentiment.PositiveThemes],
            NegativeThemes = [.. sentiment.NegativeThemes],
            SentimentMethod = sentiment.Method,
            SentimentAnalysedAt = createdAt,
            IsSampleData = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    // ------------------------------------------------------------------ media

    /// <summary>
    /// Sample media are original SVG illustrations shipped in the API's wwwroot (not copyrighted
    /// photography). Administrators replace them with real photos through the upload endpoint.
    /// </summary>
    private IEnumerable<VehicleMedia> CreateMedia(SampleData.VehicleSeed s)
    {
        var name = $"{s.Make} {s.Model}";
        (MediaKind Kind, string File, string Alt)[] items =
        [
            (MediaKind.Exterior, "side", $"{name}, side view (illustration)"),
            (MediaKind.Exterior, "front", $"{name}, front view (illustration)"),
            (MediaKind.Exterior, "rear", $"{name}, rear view (illustration)"),
            (MediaKind.Interior, "interior", $"{name}, interior (illustration)"),
            (MediaKind.Dashboard, "dashboard", $"{name}, dashboard (illustration)"),
            (MediaKind.Boot, "boot", $"{name}, boot space (illustration)"),
        ];

        return items.Select((item, i) => new VehicleMedia
        {
            Id = Guid.NewGuid(),
            Kind = item.Kind,
            Url = $"/media/sample/{s.Slug}/{item.File}.svg",
            AltText = item.Alt,
            SortOrder = i,
            IsPrimary = i == 0,
            Attribution = MediaAttribution,
            IsSampleData = true,
            CreatedAt = now,
        });
    }

    // ------------------------------------------------------------------ helpers

    private readonly HashSet<string> references = [];

    private string NewReference()
    {
        string reference;
        do
        {
            reference = BookingReference.Generate(rng);
        }
        while (!references.Add(reference));

        return reference;
    }

    private double Exponential(double mean) => -mean * Math.Log(1 - rng.NextDouble());

    private static int RoundKm(int km) => (int)(Math.Round(km / 5000.0) * 5000);

    private static string Slug(string s) => new(s.ToLowerInvariant().Where(char.IsLetter).ToArray());

    private static DateTime TruncateToMinute(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, DateTimeKind.Utc);

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
