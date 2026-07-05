using System;
using System.Collections.Generic;

namespace RimAI.Agent.Action
{
    /// <summary>
    /// The universal DTO for all game commands. Serialized by Cognition,
    /// deserialized and executed by Action. No Verse/RimWorld types in this DTO.
    /// All entity references use string IDs from the Perception snapshot.
    /// </summary>
    [Serializable]
    public class ActionCommand
    {
        /// <summary>Unique ID for traceability across layers.</summary>
        // TODO(impl): CommandId — Guid.NewGuid().ToString("N"); unique across all commands for dedup and feedback correlation
        public string CommandId { get; init; } = Guid.NewGuid().ToString("N");

        /// <summary>Whitelisted command type.</summary>
        // TODO(impl): Type — maps to registered ICommandHandler; every value has a handler in CommandDispatcher
        public ActionCommandType Type { get; init; }

        /// <summary>Which planner tier originated this command.</summary>
        // TODO(impl): SourceTier — "reactive", "tactical", or "strategic"; for logging and budget attribution
        public string SourceTier { get; init; } = "unknown";

        /// <summary>ConversationId from the Cognition session that produced this.</summary>
        // TODO(impl): ConversationId — links command to LLM conversation for debugging and log correlation
        public string ConversationId { get; init; } = "";

        /// <summary>UTC timestamp when the command was generated.</summary>
        // TODO(impl): GeneratedAt — DateTime.UtcNow; used for queue ordering within same priority
        public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

        /// <summary>Priority for queue ordering (0 = highest).</summary>
        // TODO(impl): Priority — 0=critical (rescue/draft/tend), 1=urgent, 2=normal, 3=low.
        //   Queue drains in priority order then FIFO within same priority.
        public int Priority { get; init; } = 2;

        /// <summary>Command-type-specific parameters. Schema varies by Type.</summary>
        // TODO(impl): Parameters — type-specific keys per Section 5 of action spec.
        //   E.g., SetWorkPriority requires colonist_id(string), work_type(string), priority(int 0-4).
        public Dictionary<string, object> Parameters { get; init; } = new Dictionary<string, object>();

        /// <summary>Optional human-readable reason for debugging/decision log.</summary>
        // TODO(impl): Reason — LLM-generated explanation for the command; truncated to 200 chars in decision log
        public string Reason { get; init; } = "";
    }

    /// <summary>
    /// Exhaustive whitelist. Every value maps to a registered ICommandHandler.
    /// Adding a value here requires: handler impl, tool schema update, validator update.
    /// </summary>
    public enum ActionCommandType
    {
        // --- Work & Bills ---
        // TODO(impl): SetWorkPriority — assign work priority 0-4 for colonist on a work type
        SetWorkPriority,
        // TODO(impl): AddBill — add a production bill to a table (recipe, count, optional radius/skill/repeat)
        AddBill,
        // TODO(impl): RemoveBill — remove an existing bill by ID
        RemoveBill,
        // TODO(impl): SuspendBill — suspend a bill (pause until resumed)
        SuspendBill,
        // TODO(impl): ResumeBill — resume a previously suspended bill
        ResumeBill,

        // --- Zones ---
        // TODO(impl): CreateZone — create a new zone (stockpile/growing/dumping) with cell indices
        CreateZone,
        // TODO(impl): DeleteZone — delete an existing zone by ID
        DeleteZone,
        // TODO(impl): AssignToZone — assign a colonist to a zone (allowed area)
        AssignToZone,
        // TODO(impl): UnassignFromZone — remove colonist from zone assignment
        UnassignFromZone,
        // TODO(impl): RenameZone — rename an existing zone
        RenameZone,

        // --- Designations ---
        // TODO(impl): AddDesignation — add a designation (mine/chop/harvest/deconstruct) to targets
        AddDesignation,
        // TODO(impl): RemoveDesignation — remove an existing designation
        RemoveDesignation,

        // --- Drafting & Combat ---
        // TODO(impl): DraftColonist — draft a colonist into combat mode
        DraftColonist,
        // TODO(impl): UndraftColonist — return a colonist to non-combat state
        UndraftColonist,
        // TODO(impl): MoveTo — order a colonist to move to specific cell coordinates
        MoveTo,
        // TODO(impl): AttackTarget — order a colonist to attack a specific target
        AttackTarget,
        // TODO(impl): HoldFire — order a colonist to hold fire (don't auto-engage)
        HoldFire,
        // TODO(impl): FireAtWill — order a colonist to fire at will (auto-engage)
        FireAtWill,

        // --- Medical & Rescue ---
        // TODO(impl): RescueColonist — order rescuer to carry downed colonist to bed
        RescueColonist,
        // TODO(impl): CapturePrisoner — order captor to capture a downed enemy to prisoner bed
        CapturePrisoner,
        // TODO(impl): TendPawn — order doctor to tend a patient's injuries
        TendPawn,
        // TODO(impl): PrioritizeSurgery — prioritize a specific surgery bill for execution
        PrioritizeSurgery,
        // TODO(impl): SetMedicalDefaults — set colonist's default medicine policy and max medicine level
        SetMedicalDefaults,

        // --- Equipment & Loadout ---
        // TODO(impl): EquipItem — order colonist to equip a specific item/weapon
        EquipItem,
        // TODO(impl): DropItem — order colonist to drop an equipped/carried item
        DropItem,
        // TODO(impl): SetOutfitPolicy — assign an outfit policy to a colonist
        SetOutfitPolicy,
        // TODO(impl): SetDrugPolicy — assign a drug policy to a colonist
        SetDrugPolicy,
        // TODO(impl): SetFoodPolicy — assign a food restriction to a colonist
        SetFoodPolicy,

        // --- Research ---
        // TODO(impl): SetResearchProject — set the active research project
        SetResearchProject,

        // --- Trade & Caravans ---
        // TODO(impl): InitiateTrade — initiate trade with a settlement or faction
        InitiateTrade,
        // TODO(impl): FormCaravan — form a caravan with specified colonists, destination, supplies
        FormCaravan,
        // TODO(impl): CancelCommand — cancel a previously issued command by its CommandId
        CancelCommand,

        // --- Schedule ---
        // TODO(impl): SetSchedule — set a colonist's 24-hour schedule
        SetSchedule,
        // TODO(impl): SetRecreation — set a colonist's recreation schedule
        SetRecreation,
    }
}
