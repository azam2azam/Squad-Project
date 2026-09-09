namespace Domain.Enums;

/// <summary>
/// What became of an inbound Telegram message. Values are persisted; do not renumber.
///
/// The refusals are enumerated rather than lumped into one "failed" because they need
/// different answers: an unlinked sender needs an enrolment code, an ambiguous board needs
/// a code, and an unparsed message needs the template.
/// </summary>
public enum TelegramOutcome
{
    /// <summary>Read from Telegram, not yet judged.</summary>
    Received = 0,

    /// <summary>A board was updated. The change is in that board's audit trail too.</summary>
    Applied = 1,

    /// <summary>Understood, but every value already matched — nothing to write.</summary>
    NoChange = 2,

    /// <summary>A command like /help or /boards, answered without touching a board.</summary>
    Command = 3,

    /// <summary>The sender is not linked to an application account, or the link was revoked.</summary>
    SenderNotLinked = 4,

    /// <summary>Linked, but their role or board ownership does not allow the edit.</summary>
    NotPermitted = 5,

    /// <summary>No board matched what they typed, or more than one did.</summary>
    BoardNotResolved = 6,

    /// <summary>Nothing in the message looked like an update.</summary>
    NotUnderstood = 7,

    /// <summary>Understood and permitted, but the write itself failed.</summary>
    Failed = 8,

    /// <summary>The chat is outside the configured allow-list.</summary>
    ChatNotAllowed = 9
}
