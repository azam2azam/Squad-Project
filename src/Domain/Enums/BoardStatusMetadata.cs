namespace Domain.Enums;

/// <summary>
/// Display label and badge colour for each <see cref="BoardStatus"/>.
/// Colours are the prototype design tokens; keep in step with _tokens.scss.
/// </summary>
public static class BoardStatusMetadata
{
    private static readonly IReadOnlyDictionary<BoardStatus, StatusInfo> Map =
        new Dictionary<BoardStatus, StatusInfo>
        {
            [BoardStatus.OnTrack]   = new("On Track",  "#34D399"),
            [BoardStatus.AtRisk]    = new("At Risk",   "#FBBF24"),
            [BoardStatus.Blocked]   = new("Blocked",   "#F87171"),
            [BoardStatus.InReview]  = new("In Review", "#60A5FA"),
            [BoardStatus.Delivered] = new("Delivered", "#2DD4BF")
        };

    public static StatusInfo Get(BoardStatus status) => Map[status];

    public static string Label(BoardStatus status) => Map[status].Label;

    public static string Color(BoardStatus status) => Map[status].Color;
}

public readonly record struct StatusInfo(string Label, string Color);
