namespace FMS.Domain.Enums;

/// <summary>
/// What a farm owner or manager is being refused permission to do.
/// <para>
/// This exists so the eight "Only farm owners …" refusals can share one key and one sentence.
/// The alternative is eight keys, each repeating the frame, and a ninth action added later as a
/// tenth sentence in three languages rather than one vocabulary entry.
/// </para>
/// <para>
/// It is an enum rather than a bare string because the value travels to the client and comes
/// back as a vocabulary lookup. A misspelt string literal would not throw: the client would not
/// recognise it, and the refusal would render as "Only farm owners and managers can
/// ChnageMemberRoles" — English, with the typo in it, inside a translated interface. The name
/// here is the wire value, so it is checked against the client's vocabulary by
/// <c>FarmOwnerActionVocabularyTests</c> rather than trusted.
/// </para>
/// <para>
/// A new member is a change on both sides at once — an enum member and a vocabulary entry — and
/// that test is what makes forgetting the second one a failure instead of a rendering bug.
/// </para>
/// </summary>
public enum FarmOwnerAction
{
    /// <summary>Changing which role a farm member holds.</summary>
    ChangeMemberRoles,

    /// <summary>Removing someone from the farm.</summary>
    RemoveMembers,

    /// <summary>Inviting someone to the farm.</summary>
    InviteMembers,

    /// <summary>Reading the farm's outstanding invitations.</summary>
    ViewInvitations,

    /// <summary>Withdrawing an invitation that was sent.</summary>
    RevokeInvitations,

    /// <summary>Editing the farm's own details.</summary>
    UpdateFarmDetails,

    /// <summary>Deleting the farm itself, which only an owner may do — a manager may not.</summary>
    DeleteFarms,
}