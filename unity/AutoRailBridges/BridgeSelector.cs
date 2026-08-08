using PlayerEquipment;
using Unity.Entities;

namespace AutoRailBridges
{
    /// <summary>
    /// Decides WHICH bridge to spend. This is the only file that knows the order, so making the
    /// order configurable (v1.1, once Mod Settings Menu can declare a string/list setting) is a
    /// change confined to this class.
    ///
    /// The inventory read needs no extra plumbing: LookupEquipmentUpdateData already carries
    /// BufferLookup&lt;ContainedObjectsBuffer&gt; containedObjectsBufferLookup (Pug.Other:419083),
    /// which EquipmentUpdateSystem.UpdateJob.Execute reads for equipmentUpdateAspect.entity in
    /// exactly this way (Pug.Other:419886).
    /// </summary>
    internal static class BridgeSelector
    {
        /// <summary>
        /// Ascending ObjectID, which roughly tracks Core Keeper's content progression and
        /// therefore roughly tracks scarcity: the cheapest bridge the player actually carries is
        /// spent first, and a valuable one is only touched once everything cheaper is gone.
        /// No type is excluded — excluding one would make the mod silently do nothing for a
        /// player carrying only that type.
        /// </summary>
        internal static readonly ObjectID[] Priority =
        {
            ObjectID.WoodBridge, // 4703
            ObjectID.StoneBridge, // 4707
            ObjectID.ScarletBridge, // 4712
            ObjectID.CoralBridge, // 4717
            ObjectID.GalaxiteBridge, // 4721
            ObjectID.GlassBridge, // 4724
            ObjectID.GleamWoodBridge, // 4729
            ObjectID.MetalGrateBridge, // 4773
            ObjectID.ExcavationBridge, // 4802
        };

        internal static bool IsRailEquipped(in EquipmentUpdateAspect aspect)
        {
            return aspect.equippedObjectCD.ValueRO.containedObject.objectData.objectID == ObjectID.Rail;
        }

        /// <summary>
        /// Finds the highest-priority bridge in the player's inventory. Priority order wins over
        /// slot order: the whole inventory is scanned for Priority[0] before Priority[1] is
        /// considered at all.
        /// </summary>
        internal static bool TryFind(in EquipmentUpdateAspect aspect, in LookupEquipmentUpdateData lookup, out int slotIndex, out ObjectID bridgeID)
        {
            slotIndex = -1;
            bridgeID = ObjectID.None;

            if (!lookup.containedObjectsBufferLookup.TryGetBuffer(aspect.entity, out DynamicBuffer<ContainedObjectsBuffer> inventory))
            {
                return false;
            }

            for (int p = 0; p < Priority.Length; p++)
            {
                for (int i = 0; i < inventory.Length; i++)
                {
                    ContainedObjectsBuffer slot = inventory[i];
                    if (slot.objectData.objectID == Priority[p] && slot.objectData.amount > 0)
                    {
                        slotIndex = i;
                        bridgeID = Priority[p];
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
