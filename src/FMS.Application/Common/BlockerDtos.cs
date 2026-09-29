namespace FMS.Application.Common;

/// <summary>
/// What stands in the way of a refused change, carried beside the refusal's message.
///
/// <para>
/// A conflict that names only a count ("still used by 2 gestation records") leaves the reader
/// to go and find those records. A conflict that names the records — id, what it is, enough
/// words to recognise it — can link straight to them. The count stays in the message so every
/// existing caller and test reads what it always read; the rows are additive, and a caller
/// that ignores them loses nothing.
/// </para>
/// </summary>
/// <param name="Id">The id of the row in the way — a thing the client can route to.</param>
/// <param name="Kind">What the row is, as the domain names it: "gestationRecord".</param>
/// <param name="Label">
/// Enough to recognise the row at a glance ("Shed B · expected 2026-11-30"). Deliberately
/// unlocalised detail: the client owns the words for a kind; this is only the identifying
/// string.
/// </param>
public sealed record Blocker(Guid Id, string Kind, string Label);
