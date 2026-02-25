using SchedulingAgent.Models;
using SchedulingAgent.Services;

namespace SchedulingAgent.Tests.Integration;

/// <summary>
/// Realistic Dutch office personas used across all integration test scenarios.
/// Each persona has a defined role, team membership, and typical calendar state.
/// </summary>
public static class TestPersonas
{
    // ──────────────────────────────────────────────
    // Users
    // ──────────────────────────────────────────────

    public static readonly ResolvedParticipant Sophie = new()
    {
        UserId = "user-sophie",
        DisplayName = "Sophie van den Berg",
        Email = "sophie@contoso.nl",
        IsRequired = true
    };

    public static readonly ResolvedParticipant Jan = new()
    {
        UserId = "user-jan",
        DisplayName = "Jan de Vries",
        Email = "jan@contoso.nl",
        IsRequired = true
    };

    public static readonly ResolvedParticipant Fatima = new()
    {
        UserId = "user-fatima",
        DisplayName = "Fatima El Amrani",
        Email = "fatima@contoso.nl",
        IsRequired = true
    };

    public static readonly ResolvedParticipant Pieter = new()
    {
        UserId = "user-pieter",
        DisplayName = "Pieter Jansen",
        Email = "pieter@contoso.nl",
        IsRequired = true
    };

    public static readonly ResolvedParticipant Lisa = new()
    {
        UserId = "user-lisa",
        DisplayName = "Lisa Bakker",
        Email = "lisa@contoso.nl",
        IsRequired = true
    };

    public static readonly ResolvedParticipant Daan = new()
    {
        UserId = "user-daan",
        DisplayName = "Daan Visser",
        Email = "daan@contoso.nl",
        IsRequired = true
    };

    // ──────────────────────────────────────────────
    // Groups
    // ──────────────────────────────────────────────

    public const string MarketingTeamName = "Marketing-team";
    public const string MarketingTeamId = "group-marketing";

    /// <summary>Marketing team: Fatima (manager), Jan, Lisa</summary>
    public static readonly List<ResolvedParticipant> MarketingTeamMembers =
    [
        Fatima with { ResolvedFromGroup = MarketingTeamName },
        Jan with { ResolvedFromGroup = MarketingTeamName },
        Lisa with { ResolvedFromGroup = MarketingTeamName }
    ];

    public const string EngineeringTeamName = "Engineering-team";
    public const string EngineeringTeamId = "group-engineering";

    /// <summary>Engineering team: Pieter (lead), Daan, Sophie</summary>
    public static readonly List<ResolvedParticipant> EngineeringTeamMembers =
    [
        Pieter with { ResolvedFromGroup = EngineeringTeamName },
        Daan with { ResolvedFromGroup = EngineeringTeamName },
        Sophie with { ResolvedFromGroup = EngineeringTeamName }
    ];

    // ──────────────────────────────────────────────
    // Calendar events (representing "next week Tuesday")
    // ──────────────────────────────────────────────

    public static DateTimeOffset NextTuesday =>
        DateTimeOffset.UtcNow.Date.AddDays(((int)DayOfWeek.Tuesday - (int)DateTimeOffset.UtcNow.DayOfWeek + 7) % 7 + 7).AddHours(0);

    public static DateTimeOffset NextWednesday => NextTuesday.AddDays(1);
    public static DateTimeOffset NextThursday => NextTuesday.AddDays(2);

    /// <summary>Jan: Wekelijks marketingoverleg Tue 10:00-11:00 (recurring, internal, low importance)</summary>
    public static ScheduleItem JanWeeklyMarketing => new()
    {
        UserId = Jan.UserId,
        DisplayName = Jan.DisplayName,
        Start = NextTuesday.AddHours(10),
        End = NextTuesday.AddHours(11),
        Status = "busy",
        Subject = "Wekelijks marketingoverleg",
        IsRecurring = true,
        Sensitivity = "normal",
        Importance = "low"
    };

    /// <summary>Jan: Lunch met klant Tue 12:00-13:00 (external, high importance)</summary>
    public static ScheduleItem JanExternalLunch => new()
    {
        UserId = Jan.UserId,
        DisplayName = Jan.DisplayName,
        Start = NextTuesday.AddHours(12),
        End = NextTuesday.AddHours(13),
        Status = "busy",
        Subject = "Lunch met klant Acme Corp",
        IsRecurring = false,
        Sensitivity = "private",
        Importance = "high"
    };

    /// <summary>Fatima: Focus Time Tue 09:00-12:00</summary>
    public static ScheduleItem FatimaFocusTime => new()
    {
        UserId = Fatima.UserId,
        DisplayName = Fatima.DisplayName,
        Start = NextTuesday.AddHours(9),
        End = NextTuesday.AddHours(12),
        Status = "busy",
        Subject = "Focus Time",
        IsRecurring = true,
        Sensitivity = "normal",
        Importance = "normal"
    };

    /// <summary>Fatima: 1:1 met directeur Tue 14:00-15:00 (confidential)</summary>
    public static ScheduleItem FatimaDirectorMeeting => new()
    {
        UserId = Fatima.UserId,
        DisplayName = Fatima.DisplayName,
        Start = NextTuesday.AddHours(14),
        End = NextTuesday.AddHours(15),
        Status = "busy",
        Subject = "1:1 met directeur",
        IsRecurring = false,
        Sensitivity = "confidential",
        Importance = "high"
    };

    /// <summary>Pieter: Sprint planning Wed 09:00-10:30 (recurring, internal)</summary>
    public static ScheduleItem PieterSprintPlanning => new()
    {
        UserId = Pieter.UserId,
        DisplayName = Pieter.DisplayName,
        Start = NextWednesday.AddHours(9),
        End = NextWednesday.AddHours(10).AddMinutes(30),
        Status = "busy",
        Subject = "Sprint planning",
        IsRecurring = true,
        Sensitivity = "normal",
        Importance = "normal"
    };

    /// <summary>Pieter: Code review Tue 10:00-11:00 (recurring, internal, low importance)</summary>
    public static ScheduleItem PieterCodeReview => new()
    {
        UserId = Pieter.UserId,
        DisplayName = Pieter.DisplayName,
        Start = NextTuesday.AddHours(10),
        End = NextTuesday.AddHours(11),
        Status = "busy",
        Subject = "Code review sessie",
        IsRecurring = true,
        Sensitivity = "normal",
        Importance = "low"
    };

    /// <summary>Lisa: Hele dag conferentie Wed (all-day event)</summary>
    public static ScheduleItem LisaConference => new()
    {
        UserId = Lisa.UserId,
        DisplayName = Lisa.DisplayName,
        Start = NextWednesday,
        End = NextWednesday.AddDays(1),
        Status = "oof",
        Subject = "Marketing Conferentie Amsterdam",
        IsRecurring = false,
        Sensitivity = "normal",
        Importance = "high"
    };

    /// <summary>Daan: Tandarts Tue 10:00-11:00 (personal, private)</summary>
    public static ScheduleItem DaanDentist => new()
    {
        UserId = Daan.UserId,
        DisplayName = Daan.DisplayName,
        Start = NextTuesday.AddHours(10),
        End = NextTuesday.AddHours(11),
        Status = "busy",
        Subject = "Tandarts",
        IsRecurring = false,
        Sensitivity = "private",
        Importance = "normal"
    };

    /// <summary>Sophie: free all of next week (no events)</summary>
    public static List<ScheduleItem> SophieSchedule => [];

    /// <summary>All calendar events for schedule lookups</summary>
    public static List<ScheduleItem> AllScheduleItems =>
    [
        JanWeeklyMarketing,
        JanExternalLunch,
        FatimaFocusTime,
        FatimaDirectorMeeting,
        PieterSprintPlanning,
        PieterCodeReview,
        LisaConference,
        DaanDentist
    ];
}
