using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Application.Identity;
using SmartDrive.Domain.Analytics;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Application.Services;

/// <summary>
/// Reviews are tied to a completed rental (one per booking), written by the renter, analysed for
/// sentiment on save, and visible publicly unless an administrator hides them. Reported reviews stay
/// visible until moderated so the report button cannot be used to silence criticism.
/// </summary>
public sealed class ReviewService(
    SmartDriveDbContext db,
    RentalPolicy policy,
    TimeProvider clock,
    AuditWriter audit,
    Notifier notifier)
{
    private static readonly ReviewStatus[] PublicStatuses = [ReviewStatus.Published, ReviewStatus.Reported];

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<ReviewDto>> ListAsync(ReviewQuery q, Guid? viewerId, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalise(q.Page, q.PageSize, 50);
        var query = db.Reviews.AsNoTracking().Include(r => r.Vehicle).Where(r => PublicStatuses.Contains(r.Status));
        if (q.VehicleId is { } vehicleId)
        {
            query = query.Where(r => r.VehicleId == vehicleId);
        }

        if (q.Rating is >= 1 and <= 5)
        {
            query = query.Where(r => r.Overall == q.Rating);
        }

        query = q.Sort switch
        {
            "oldest" => query.OrderBy(r => r.CreatedAt),
            "highest" => query.OrderByDescending(r => r.Overall).ThenByDescending(r => r.CreatedAt),
            "lowest" => query.OrderBy(r => r.Overall).ThenByDescending(r => r.CreatedAt),
            _ => query.OrderByDescending(r => r.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var reviews = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var authors = await AuthorsAsync(reviews.Select(r => r.CustomerId), ct);
        return new PagedResult<ReviewDto>(reviews.Select(r => ToDto(r, authors, viewerId)).ToList(), page, pageSize, total);
    }

    public async Task<ReviewDto> CreateAsync(string bookingReference, ReviewInput input, Guid userId, CancellationToken ct)
    {
        Validate(input);
        var booking = await db.Bookings.Include(b => b.Review).Include(b => b.Vehicle)
                          .FirstOrDefaultAsync(b => b.Reference == bookingReference, ct)
                      ?? throw DomainException.NotFound("Booking");
        if (booking.CustomerId != userId)
        {
            throw DomainException.NotFound("Booking");
        }

        var problem = ReviewEligibility.Check(booking.CustomerId, booking.Status, booking.ReturnAt, booking.Review is not null, userId, Now, policy);
        if (problem is { } reason)
        {
            throw new DomainException($"review.{reason.ToString().ToLowerInvariant()}", ReviewEligibility.Describe(reason),
                reason == ReviewIneligibility.AlreadyReviewed ? ErrorKind.Conflict : ErrorKind.Validation);
        }

        var now = Now;
        var review = new Review
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            VehicleId = booking.VehicleId,
            CustomerId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Apply(review, input);
        db.Reviews.Add(review);
        audit.Write(userId, "review.created", nameof(Review), review.Id.ToString(), new { booking.Reference, review.Overall });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique index on BookingId caught a duplicate submitted at the same moment.
            throw new DomainException("review.alreadyreviewed", ReviewEligibility.Describe(ReviewIneligibility.AlreadyReviewed), ErrorKind.Conflict);
        }

        review.Vehicle = booking.Vehicle;
        var authors = await AuthorsAsync([userId], ct);
        return ToDto(review, authors, userId);
    }

    public async Task<ReviewDto> UpdateAsync(Guid id, ReviewInput input, Guid userId, CancellationToken ct)
    {
        Validate(input);
        var review = await OwnedAsync(id, userId, ct);
        if (!CanEdit(review))
        {
            throw new DomainException("review.edit_window", $"Reviews can be edited for {policy.ReviewEditWindowDays} days after posting.", ErrorKind.Conflict);
        }

        Apply(review, input);
        review.UpdatedAt = Now;
        audit.Write(userId, "review.updated", nameof(Review), id.ToString());
        await db.SaveChangesAsync(ct);
        var authors = await AuthorsAsync([userId], ct);
        return ToDto(review, authors, userId);
    }

    /// <summary>Removes the review from public view. The record is kept (status Removed) for audit purposes.</summary>
    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var review = await OwnedAsync(id, userId, ct);
        review.Status = ReviewStatus.Removed;
        review.UpdatedAt = Now;
        audit.Write(userId, "review.removed_by_author", nameof(Review), id.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task ReportAsync(Guid id, string reason, Guid userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new DomainException("review.report_reason", "Please give a reason of up to 500 characters.");
        }

        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id && PublicStatuses.Contains(r.Status), ct)
                     ?? throw DomainException.NotFound("Review");
        if (review.CustomerId == userId)
        {
            throw new DomainException("review.report_own", "You cannot report your own review.");
        }

        if (await db.ReviewReports.AnyAsync(r => r.ReviewId == id && r.ReporterId == userId, ct))
        {
            throw new DomainException("review.already_reported", "You have already reported this review.", ErrorKind.Conflict);
        }

        db.ReviewReports.Add(new ReviewReport { Id = Guid.NewGuid(), ReviewId = id, ReporterId = userId, Reason = reason.Trim(), CreatedAt = Now });
        review.Status = ReviewStatus.Reported;
        audit.Write(userId, "review.reported", nameof(Review), id.ToString());
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ administration

    public async Task<PagedResult<AdminReviewDto>> AdminListAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalise(page, pageSize);
        var query = db.Reviews.AsNoTracking().Include(r => r.Vehicle).Include(r => r.Booking).Include(r => r.Reports).AsSplitQuery();
        if (Parse.OptionalEnum<ReviewStatus>(status, "review status") is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        var total = await query.CountAsync(ct);
        var reviews = await query.OrderByDescending(r => r.Status == ReviewStatus.Reported).ThenByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var authors = await AuthorsAsync(reviews.Select(r => r.CustomerId), ct);

        var items = reviews.Select(r => new AdminReviewDto(
            ToDto(r, authors, null),
            r.Status.ToString(),
            authors.TryGetValue(r.CustomerId, out var a) ? a.FullName : "Unknown",
            r.Booking?.Reference ?? "",
            r.Reports.Count(x => x.ResolvedAt == null),
            r.Reports.Where(x => x.ResolvedAt == null).Select(x => x.Reason).ToList(),
            r.SentimentLabel,
            r.SentimentScore,
            r.PositiveThemes,
            r.NegativeThemes,
            r.ModerationNote)).ToList();

        return new PagedResult<AdminReviewDto>(items, page, pageSize, total);
    }

    public async Task ModerateAsync(Guid id, ModerateReviewRequest req, Guid adminId, CancellationToken ct)
    {
        var review = await db.Reviews.Include(r => r.Reports).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound("Review");
        if (review.Status == ReviewStatus.Removed)
        {
            throw new DomainException("review.removed", "This review was removed by its author.", ErrorKind.Conflict);
        }

        var now = Now;
        review.Status = req.Action?.ToLowerInvariant() switch
        {
            "publish" or "restore" or "approve" => ReviewStatus.Published,
            "hide" => ReviewStatus.Hidden,
            _ => throw new DomainException("review.moderation_action", "Action must be 'publish' or 'hide'."),
        };
        review.ModeratedAt = now;
        review.ModeratedByUserId = adminId;
        review.ModerationNote = req.Note?.Trim();
        foreach (var report in review.Reports.Where(r => r.ResolvedAt is null))
        {
            report.ResolvedAt = now;
        }

        audit.Write(adminId, $"review.{review.Status.ToString().ToLowerInvariant()}", nameof(Review), id.ToString(), new { req.Note });
        if (review.Status == ReviewStatus.Hidden)
        {
            notifier.Notify(review.CustomerId, "Review hidden", "One of your reviews was hidden by a moderator after a review of its content.");
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Re-runs sentiment analysis on every review (used after changing the analyser).</summary>
    public async Task<int> ReanalyseAllAsync(CancellationToken ct)
    {
        var reviews = await db.Reviews.ToListAsync(ct);
        foreach (var r in reviews)
        {
            ApplySentiment(r);
        }

        await db.SaveChangesAsync(ct);
        return reviews.Count;
    }

    // ------------------------------------------------------------------ helpers

    private static void Validate(ReviewInput input)
    {
        int[] ratings = [input.Overall, input.Condition, input.Cleanliness, input.Comfort, input.Value];
        if (ratings.Any(r => r is < 1 or > 5))
        {
            throw new DomainException("review.rating", "Every rating must be between 1 and 5 stars.");
        }

        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 120)
        {
            throw new DomainException("review.title", "Please add a title of up to 120 characters.");
        }

        if (string.IsNullOrWhiteSpace(input.Comment) || input.Comment.Trim().Length is < 10 or > 2000)
        {
            throw new DomainException("review.comment", "Please write between 10 and 2,000 characters.");
        }
    }

    private void Apply(Review review, ReviewInput input)
    {
        review.Overall = input.Overall;
        review.Condition = input.Condition;
        review.Cleanliness = input.Cleanliness;
        review.Comfort = input.Comfort;
        review.Value = input.Value;
        review.Title = input.Title.Trim();
        review.Comment = input.Comment.Trim();
        ApplySentiment(review);
    }

    private void ApplySentiment(Review review)
    {
        var result = SentimentAnalyzer.Analyse(review.Title, review.Comment);
        review.SentimentLabel = result.Label;
        review.SentimentScore = result.Score;
        review.PositiveThemes = result.PositiveThemes.ToList();
        review.NegativeThemes = result.NegativeThemes.ToList();
        review.SentimentMethod = result.Method;
        review.SentimentAnalysedAt = Now;
    }

    private bool CanEdit(Review review) =>
        review.Status is ReviewStatus.Published or ReviewStatus.Reported && Now <= review.CreatedAt.AddDays(policy.ReviewEditWindowDays);

    private async Task<Review> OwnedAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var review = await db.Reviews.Include(r => r.Vehicle).FirstOrDefaultAsync(r => r.Id == id, ct);
        return review is not null && review.CustomerId == userId && review.Status != ReviewStatus.Removed
            ? review
            : throw DomainException.NotFound("Review");
    }

    private async Task<Dictionary<Guid, AppUser>> AuthorsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
    }

    private ReviewDto ToDto(Review r, Dictionary<Guid, AppUser> authors, Guid? viewerId)
    {
        var author = authors.TryGetValue(r.CustomerId, out var a) ? Mapping.AuthorName(a.FirstName, a.LastName) : "SmartDrive customer";
        var mine = viewerId == r.CustomerId;
        return new ReviewDto(
            r.Id, r.VehicleId, r.Vehicle?.DisplayName ?? "", author,
            r.Overall, r.Condition, r.Cleanliness, r.Comfort, r.Value, r.Title, r.Comment,
            Mapping.Local(r.CreatedAt), r.UpdatedAt > r.CreatedAt.AddMinutes(1) ? Mapping.Local(r.UpdatedAt) : null,
            mine, mine && CanEdit(r), r.IsSampleData);
    }
}
