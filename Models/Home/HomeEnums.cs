namespace Household.Api.Models.Home;

public enum ScheduleType
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    IntervalDays = 3,
}

public enum TimeOfDaySlot
{
    Morning = 0,
    Night = 1,
    Anytime = 2,
}

public enum TaskInstanceStatus
{
    Pending = 0,
    Completed = 1,
    Skipped = 2,
    Failed = 3,
}

public enum IssueStatus
{
    Open = 0,
    InProgress = 1,
    Done = 2,
    WontFix = 3,
}
