namespace ActualChat.Users;

public static class CoachWeek
{
    public static Moment StartOf(Moment day)
    {
        // ISO weeks start on Monday, in UTC like the day rows
        var offset = ((int)day.ToDateTime().DayOfWeek + 6) % 7;
        return day - TimeSpan.FromDays(offset);
    }
}
