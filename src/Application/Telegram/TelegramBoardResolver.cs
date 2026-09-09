using Domain.Entities;

namespace Application.Telegram;

/// <summary>
/// Works out which board somebody meant.
///
/// The order is widest-confidence-first, and it stops at the first tier that produces
/// exactly one answer. A tier that produces several is reported as ambiguous rather than
/// resolved arbitrarily — writing a status onto the wrong board is the one failure this
/// integration must never have, and "which one did you mean?" costs the sender five
/// seconds.
/// </summary>
public static class TelegramBoardResolver
{
    public static BoardMatch Resolve(IReadOnlyList<Board> boards, string? reference)
    {
        var term = (reference ?? string.Empty).Trim();

        if (term.Length == 0)
        {
            return BoardMatch.None(
                "Which board? Start the message with its code, for example: #update DIS");
        }

        // 1. The code, which is what the code exists for.
        var byCode = boards
            .Where(b => string.Equals(b.Code, term, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byCode.Count == 1) return BoardMatch.Found(byCode[0]);

        // 2. The exact title, for anyone who copies it out of the app.
        var byTitle = boards
            .Where(b => string.Equals(b.Title, term, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byTitle.Count == 1) return BoardMatch.Found(byTitle[0]);

        // 3. A title that starts with what they typed — "Discharge" for the long one.
        var byPrefix = boards
            .Where(b => b.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byPrefix.Count == 1) return BoardMatch.Found(byPrefix[0]);

        // 4. Anywhere in the title or the product, the loosest tier and the likeliest to
        //    be ambiguous — which is why it is last and still checked for a single answer.
        var byContains = boards
            .Where(b => b.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || b.Product.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byContains.Count == 1) return BoardMatch.Found(byContains[0]);

        var candidates = byPrefix.Count > 1 ? byPrefix : byContains;

        if (candidates.Count > 1)
        {
            var names = candidates
                .Take(5)
                .Select(b => b.Code ?? b.Title);

            return BoardMatch.None(
                $"\"{term}\" matches {candidates.Count} boards: {string.Join(", ", names)}. "
                + "Use the code.");
        }

        return BoardMatch.None(
            $"I couldn't find a board matching \"{term}\". Send /boards to see the codes.");
    }
}

/// <summary>Either a board, or the sentence explaining why there isn't one.</summary>
public sealed record BoardMatch(Board? Board, string Message)
{
    public static BoardMatch Found(Board board) => new(board, string.Empty);

    public static BoardMatch None(string message) => new(null, message);
}
