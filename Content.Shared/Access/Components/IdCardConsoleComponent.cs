using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Roles; // Frontier

namespace Content.Shared.Access.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedIdCardConsoleSystem))]
public sealed partial class IdCardConsoleComponent : Component
{
    public static string PrivilegedIdCardSlotId = "IdCardConsole-privilegedId";
    public static string TargetIdCardSlotId = "IdCardConsole-targetId";

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();

    [DataField]
    public ItemSlot TargetIdSlot = new();

    [Serializable, NetSerializable]
    public sealed class WriteToTargetIdMessage : BoundUserInterfaceMessage
    {
        public readonly string FullName;
        public readonly string JobTitle;
        public readonly List<ProtoId<AccessLevelPrototype>> AccessList;
        public readonly ProtoId<JobPrototype> JobPrototype; // Frontier: AccessPrototype<JobPrototype

        public WriteToTargetIdMessage(string fullName, string jobTitle, List<ProtoId<AccessLevelPrototype>> accessList, ProtoId<JobPrototype> jobPrototype) // Frontier: jobProtoype - AccessPrototype<JobPrototype
        {
            FullName = fullName;
            JobTitle = jobTitle;
            AccessList = accessList;
            JobPrototype = jobPrototype;
        }
    }

    // Put this on shared so we just send the state once in PVS range rather than every time the UI updates.

    [DataField, AutoNetworkedField]
    public List<ProtoId<AccessLevelPrototype>> AccessLevels = new()
    {
        "Armory",
        //"Atmospherics",
        "Bailiff", // Frontier
        //"Bar",
        "Brig",
        "Brigmedic", // Frontier
        "Captain",
        //"Cargo",
        //"Chapel",
        //"Chemistry",
        //"ChiefMedicalOfficer",
        "Command",
        //"Cryogenics",
        "Detective", // Frontier: moved into alphabetical order
        "Engineering",
        "External",
        "Frontier", // Frontier
        //"Hydroponics",
        "Janitor",
        //"Kitchen",
        //"Lawyer",
        "Mail", // Frontier
        "Maintenance",
        "Medical",
        "Mercenary", // Frontier
        "ChiefEngineer", // Frontier: moved down, alphabetic w.r.t. "Plant Manager"
        //"Quartermaster",
        //"Research",
        //"ResearchDirector",
        //"Salvage",
        "Security",
        "Sergeant", // Frontier
        "Service",
        "HeadOfSecurity", // Frontier: moved down, alphabetic w.r.t. "Sheriff"
        "HeadOfPersonnel", // Frontier: moved down, alphabetic w.r.t. "Station Representative"
        "StationTrafficController", // Frontier
        //"Theatre",
    };

    [Serializable, NetSerializable]
    public sealed class IdCardConsoleBoundUserInterfaceState : BoundUserInterfaceState
    {
        public readonly string PrivilegedIdName;
        public readonly bool IsPrivilegedIdPresent;
        public readonly bool IsPrivilegedIdAuthorized;
        public readonly bool IsTargetIdPresent;
        public readonly string TargetIdName;
        public readonly string? TargetIdFullName;
        public readonly string? TargetIdJobTitle;
        public readonly bool HasOwnedShuttle; // Frontier
        public readonly string?[]? TargetShuttleNameParts; // Frontier
        public readonly List<ProtoId<AccessLevelPrototype>>? TargetIdAccessList;
        public readonly List<ProtoId<AccessLevelPrototype>>? AllowedModifyAccessList;
        public readonly ProtoId<JobPrototype> TargetIdJobPrototype; // Frontier: AccessLevelPrototype<JobPrototype

        public IdCardConsoleBoundUserInterfaceState(bool isPrivilegedIdPresent,
            bool isPrivilegedIdAuthorized,
            bool isTargetIdPresent,
            string? targetIdFullName,
            string? targetIdJobTitle,
            bool hasOwnedShuttle,
            string?[]? targetShuttleNameParts,
            List<ProtoId<AccessLevelPrototype>>? targetIdAccessList,
            List<ProtoId<AccessLevelPrototype>>? allowedModifyAccessList,
            ProtoId<JobPrototype> targetIdJobPrototype, // Frontier: AccessLevelPrototype<JobPrototype
            string privilegedIdName,
            string targetIdName)
        {
            IsPrivilegedIdPresent = isPrivilegedIdPresent;
            IsPrivilegedIdAuthorized = isPrivilegedIdAuthorized;
            IsTargetIdPresent = isTargetIdPresent;
            TargetIdFullName = targetIdFullName;
            TargetIdJobTitle = targetIdJobTitle;
            HasOwnedShuttle = hasOwnedShuttle;
            TargetShuttleNameParts = targetShuttleNameParts;
            TargetIdAccessList = targetIdAccessList;
            AllowedModifyAccessList = allowedModifyAccessList;
            TargetIdJobPrototype = targetIdJobPrototype;
            PrivilegedIdName = privilegedIdName;
            TargetIdName = targetIdName;
        }
    }

    [Serializable, NetSerializable]
    public enum IdCardConsoleUiKey : byte
    {
        Key,
    }
}
