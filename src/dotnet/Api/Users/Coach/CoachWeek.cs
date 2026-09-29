namespace ActualChat.Users;

public static class CoachWeek
{
    // ISO weeks start on Monday, in UTC like the day rows
    public static Moment StartOf(Moment day)
    {
        var offset = ((int)day.ToDateTime().DayOfWeek + 6) % 7;
        return day - TimeSpan.FromDays(offset);
    }
}
