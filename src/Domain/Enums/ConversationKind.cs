namespace Domain.Enums;

/// <summary>
/// What a conversation is attached to.
///
/// The three are genuinely different audiences, which is why they are not one "channel"
/// type with a nullable link. A board conversation is about one initiative and dies with
/// it; a squad conversation follows the people across every board they run — in this
/// database fourteen squads carry thirty boards, so "Nanditha" spans seven of them and a
/// board channel could never reach that group; a direct conversation is two people and
/// nobody else, ever.
/// </summary>
public enum ConversationKind
{
    /// <summary>One board. Anyone who may read the board may read the conversation.</summary>
    Board = 1,

    /// <summary>One squad, across every board it runs.</summary>
    Squad = 2,

    /// <summary>Exactly two people. Not joinable, not listable by anyone else.</summary>
    Direct = 3
}
