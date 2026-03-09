using System.Collections.Generic;

namespace ArchipelagoIntegration
{
    public enum ApTier { Tier1 = 1, Tier2, Tier3, Tier4, Tier5 }

    /// <summary>
    /// Maps Timberborn blueprint TemplateName values to Archipelago location names,
    /// with tier assignments matching Rules.py for the AP Shop progression.
    /// Generated from the Folktails blueprint JSONs (ScienceCost > 0 only).
    /// </summary>
    internal static class ApBuildingLocations
    {
        private static readonly Dictionary<string, string> TemplateToLocation = new()
        {
            { "Agora.Folktails", "Science: Agora" },
            { "AquaticFarmhouse.Folktails", "Science: Aquatic Farmhouse" },
            { "AquiferDrill.Folktails", "Science: Aquifer Drill" },
            { "BadwaterDome.Folktails", "Science: Badwater Dome" },
            { "BadwaterPump.Folktails", "Science: Badwater Pump" },
            { "BadwaterRig.Folktails", "Science: Badwater Rig" },
            { "Bakery.Folktails", "Science: Bakery" },
            { "BeaverStatue.Folktails", "Science: Beaver Statue" },
            { "Beehive.Folktails", "Science: Beehive" },
            { "Bench.Folktails", "Science: Bench" },
            { "BotAssembler.Folktails", "Science: Bot Assembler" },
            { "BotPartFactory.Folktails", "Science: Bot Part Factory" },
            { "BrazierOfBonding.Folktails", "Science: Brazier of Bonding" },
            { "BuildersHut.Folktails", "Science: Builders' Hut" },
            { "BulletinPole.Folktails", "Science: Bulletin Pole" },
            { "Carousel.Folktails", "Science: Carousel" },
            { "Centrifuge.Folktails", "Science: Centrifuge" },
            { "Chronometer.Folktails", "Science: Chronometer" },
            { "Clutch.Folktails", "Science: Clutch" },
            { "ContaminationBarrier.Folktails", "Science: Contamination Barrier" },
            { "ContaminationSensor.Folktails", "Science: Contamination Sensor" },
            { "ContemplationSpot.Folktails", "Science: Contemplation Spot" },
            { "DanceHall.Folktails", "Science: Dance Hall" },
            { "DepthSensor.Folktails", "Science: Depth Sensor" },
            { "Detailer.Folktails", "Science: Detailer" },
            { "Detonator.Folktails", "Science: Detonator" },
            { "DirtExcavator.Folktails", "Science: Dirt Excavator" },
            { "DistrictCrossing.Folktails", "Science: District Crossing" },
            { "DoubleDynamite.Folktails", "Science: Double Dynamite" },
            { "DoubleFloodgate.Folktails", "Science: Double Floodgate" },
            { "DoubleLodge.Folktails", "Science: Double Lodge" },
            { "DoublePlatform.Folktails", "Science: Double Platform" },
            { "Dynamite.Folktails", "Science: Dynamite" },
            { "EarthRecultivator.Folktails", "Science: Earth Recultivator" },
            { "ExplosivesFactory.Folktails", "Science: Explosives Factory" },
            { "FarmerMonument.Folktails", "Science: Farmer Monument" },
            { "FireworkLauncher.Folktails", "Science: Firework Launcher" },
            { "Floodgate.Folktails", "Science: Floodgate" },
            { "FlowSensor.Folktails", "Science: Flow Sensor" },
            { "FluidDump.Folktails", "Science: Fluid Dump" },
            { "Forester.Folktails", "Science: Forester" },
            { "FountainOfJoy.Folktails", "Science: Fountain of Joy" },
            { "Gate.Folktails", "Science: Gate" },
            { "GearWorkshop.Folktails", "Science: Gear Workshop" },
            { "GeothermalEngine.Folktails", "Science: Geothermal Engine" },
            { "GravityBattery.Folktails", "Science: Gravity Battery" },
            { "Gristmill.Folktails", "Science: Gristmill" },
            { "Hammock.Folktails", "Science: Hammock" },
            { "Hedge.Folktails", "Science: Hedge" },
            { "Herbalist.Folktails", "Science: Herbalist" },
            { "HttpAdapter.Folktails", "Science: HTTP Adapter" },
            { "HttpLever.Folktails", "Science: HTTP Lever" },
            { "ImpermeableFloor.Folktails", "Science: Impermeable Floor" },
            { "Indicator.Folktails", "Science: Indicator" },
            { "Lantern.Folktails", "Science: Lantern" },
            { "LargeTank.Folktails", "Science: Large Tank" },
            { "LargeWarehouse.Folktails", "Science: Large Warehouse" },
            { "LargeWaterPump.Folktails", "Science: Large Water Pump" },
            { "LargeWindTurbine.Folktails", "Science: Large Wind Turbine" },
            { "Levee.Folktails", "Science: Levee" },
            { "Lever.Folktails", "Science: Lever" },
            { "Lido.Folktails", "Science: Lido" },
            { "MechanicalFluidPump.Folktails", "Science: Mechanical Fluid Pump" },
            { "MedicalBed.Folktails", "Science: Medical Bed" },
            { "MediumTank.Folktails", "Science: Medium Tank" },
            { "Memory.Folktails", "Science: Memory" },
            { "MetalPlatform3x3.Folktails", "Science: Metal Platform 3x3" },
            { "MetalPlatform5x5.Folktails", "Science: Metal Platform 5x5" },
            { "Mine.Folktails", "Science: Mine" },
            { "MiniLodge.Folktails", "Science: Mini Lodge" },
            { "MudPit.Folktails", "Science: Mud Pit" },
            { "Observatory.Folktails", "Science: Observatory" },
            { "Overhang2x1.Folktails", "Science: Overhang 2x1" },
            { "Overhang3x1.Folktails", "Science: Overhang 3x1" },
            { "Overhang4x1.Folktails", "Science: Overhang 4x1" },
            { "Overhang5x1.Folktails", "Science: Overhang 5x1" },
            { "Overhang6x1.Folktails", "Science: Overhang 6x1" },
            { "PaperMill.Folktails", "Science: Paper Mill" },
            { "Platform.Folktails", "Science: Platform" },
            { "PoleBanner.Folktails", "Science: Pole Banner" },
            { "PopulationCounter.Folktails", "Science: Population Counter" },
            { "PowerMeter.Folktails", "Science: Power Meter" },
            { "PrintingPress.Folktails", "Science: Printing Press" },
            { "Refinery.Folktails", "Science: Refinery" },
            { "Relay.Folktails", "Science: Relay" },
            { "ResourceCounter.Folktails", "Science: Resource Counter" },
            { "Roof1x1.Folktails", "Science: Roof 1x1" },
            { "Roof1x2.Folktails", "Science: Roof 1x2" },
            { "Roof2x2.Folktails", "Science: Roof 2x2" },
            { "Roof2x3.Folktails", "Science: Roof 2x3" },
            { "Roof3x2.Folktails", "Science: Roof 3x2" },
            { "Scarecrow.Folktails", "Science: Scarecrow" },
            { "ScavengerFlag.Folktails", "Science: Scavenger Flag" },
            { "ScienceCounter.Folktails", "Science: Science Counter" },
            { "Shower.Folktails", "Science: Shower" },
            { "Sluice.Folktails", "Science: Sluice" },
            { "Smelter.Folktails", "Science: Smelter" },
            { "Speaker.Folktails", "Science: Speaker" },
            { "SpiralStairs.Folktails", "Science: Spiral Stairs" },
            { "SquareBanner.Folktails", "Science: Square Banner" },
            { "Stairs.Folktails", "Science: Stairs" },
            { "StreamGauge.Folktails", "Science: Stream Gauge" },
            { "SuspensionBridge1x1.Folktails", "Science: Suspension Bridge 1x1" },
            { "SuspensionBridge2x1.Folktails", "Science: Suspension Bridge 2x1" },
            { "SuspensionBridge3x1.Folktails", "Science: Suspension Bridge 3x1" },
            { "SuspensionBridge4x1.Folktails", "Science: Suspension Bridge 4x1" },
            { "SuspensionBridge5x1.Folktails", "Science: Suspension Bridge 5x1" },
            { "SuspensionBridge6x1.Folktails", "Science: Suspension Bridge 6x1" },
            { "TappersShack.Folktails", "Science: Tapper's Shack" },
            { "TerrainBlock.Folktails", "Science: Terrain Block" },
            { "Timer.Folktails", "Science: Timer" },
            { "TripleDynamite.Folktails", "Science: Triple Dynamite" },
            { "TripleFloodgate.Folktails", "Science: Triple Floodgate" },
            { "TripleLodge.Folktails", "Science: Triple Lodge" },
            { "TriplePlatform.Folktails", "Science: Triple Platform" },
            { "Tunnel.Folktails", "Science: Tunnel" },
            { "UndergroundPile.Folktails", "Science: Underground Pile" },
            { "Valve.Folktails", "Science: Valve" },
            { "VerticalPowerShaft.Folktails", "Science: Vertical Power Shaft" },
            { "WeatherStation.Folktails", "Science: Weather Station" },
            { "Weathervane.Folktails", "Science: Weathervane" },
            { "WindTurbine.Folktails", "Science: Wind Turbine" },
            { "WoodFence.Folktails", "Science: Wood Fence" },
            { "WoodWorkshop.Folktails", "Science: Wood Workshop" },
            { "ZiplineBeam.Folktails", "Science: Zipline Beam" },
            { "ZiplinePylon.Folktails", "Science: Zipline Pylon" },
            { "ZiplineStation.Folktails", "Science: Zipline Station" },
        };

        // -----------------------------------------------------------------
        // Tier assignments — must match Rules.py TIER*_SCIENCE_LOCS exactly
        // -----------------------------------------------------------------

        private static readonly HashSet<string> Tier1Locations = new()
        {
            "Science: Forester", "Science: Mini Lodge", "Science: Medium Tank",
            "Science: Levee", "Science: Vertical Power Shaft", "Science: Shower",
            "Science: Medical Bed", "Science: Contemplation Spot", "Science: Stairs",
            "Science: Platform", "Science: Builders' Hut", "Science: Lever",
            "Science: Roof 1x1", "Science: Bench", "Science: Roof 1x2",
            "Science: Lantern", "Science: Flow Sensor", "Science: Relay",
        };

        private static readonly HashSet<string> Tier2Locations = new()
        {
            "Science: Gear Workshop", "Science: Aquatic Farmhouse", "Science: Bakery",
            "Science: Gristmill", "Science: Hammock", "Science: Roof 2x2",
            "Science: Wind Turbine", "Science: Geothermal Engine", "Science: Floodgate",
            "Science: Impermeable Floor", "Science: Double Lodge", "Science: Large Warehouse",
            "Science: Badwater Pump", "Science: Fluid Dump", "Science: Double Floodgate",
            "Science: Paper Mill", "Science: Lido", "Science: Suspension Bridge 1x1",
            "Science: Double Platform", "Science: Gate", "Science: Triple Platform",
            "Science: Suspension Bridge 2x1", "Science: Herbalist", "Science: Triple Lodge",
            "Science: Hedge", "Science: Roof 2x3", "Science: Roof 3x2",
            "Science: Stream Gauge", "Science: Wood Fence", "Science: Chronometer",
            "Science: Depth Sensor", "Science: Population Counter", "Science: Scarecrow",
            "Science: Weathervane", "Science: Resource Counter", "Science: Science Counter",
            "Science: Weather Station",
        };

        private static readonly HashSet<string> Tier3Locations = new()
        {
            "Science: Scavenger Flag", "Science: Smelter", "Science: Printing Press",
            "Science: Refinery", "Science: Bot Part Factory", "Science: Large Water Pump",
            "Science: Aquifer Drill", "Science: Contamination Barrier",
            "Science: Explosives Factory", "Science: Valve", "Science: Triple Floodgate",
            "Science: Clutch", "Science: Gravity Battery", "Science: Agora",
            "Science: Beehive", "Science: Tapper's Shack", "Science: Suspension Bridge 3x1",
            "Science: Overhang 2x1", "Science: Spiral Stairs", "Science: Zipline Pylon",
            "Science: Contamination Sensor", "Science: Indicator", "Science: Speaker",
            "Science: Detonator", "Science: Overhang 3x1", "Science: Suspension Bridge 4x1",
            "Science: Zipline Beam", "Science: Zipline Station", "Science: Power Meter",
            "Science: Timer", "Science: Firework Launcher", "Science: Large Tank",
            "Science: District Crossing", "Science: Carousel", "Science: Centrifuge",
            "Science: Wood Workshop", "Science: Bulletin Pole", "Science: Beaver Statue",
            "Science: Pole Banner", "Science: Square Banner",
        };

        private static readonly HashSet<string> Tier4Locations = new()
        {
            "Science: Bot Assembler", "Science: Observatory", "Science: Dynamite",
            "Science: Double Dynamite", "Science: Terrain Block", "Science: Triple Dynamite",
            "Science: Dirt Excavator", "Science: Tunnel", "Science: Large Wind Turbine",
            "Science: Detailer", "Science: Dance Hall", "Science: Mud Pit",
            "Science: Underground Pile", "Science: Mechanical Fluid Pump",
            "Science: Badwater Dome", "Science: Metal Platform 3x3",
            "Science: Overhang 4x1", "Science: Suspension Bridge 5x1",
            "Science: Memory", "Science: Farmer Monument", "Science: Brazier of Bonding",
            "Science: Metal Platform 5x5", "Science: Overhang 5x1",
            "Science: Suspension Bridge 6x1", "Science: Overhang 6x1",
            "Science: Earth Recultivator",
        };

        private static readonly HashSet<string> Tier5Locations = new()
        {
            "Science: Mine", "Science: Badwater Rig", "Science: Fountain of Joy",
            "Science: HTTP Lever", "Science: HTTP Adapter",
        };

        // -----------------------------------------------------------------
        // Tier gate predicates — check received AP items
        // Mirrors Rules.py tier helpers: _tier1 through _tier5
        // -----------------------------------------------------------------

        public static ApTier GetTier(string locationName)
        {
            if (Tier1Locations.Contains(locationName)) return ApTier.Tier1;
            if (Tier2Locations.Contains(locationName)) return ApTier.Tier2;
            if (Tier3Locations.Contains(locationName)) return ApTier.Tier3;
            if (Tier4Locations.Contains(locationName)) return ApTier.Tier4;
            if (Tier5Locations.Contains(locationName)) return ApTier.Tier5;
            return ApTier.Tier1; // fallback
        }

        /// <summary>
        /// Returns true if the given tier is accessible based on received AP items.
        /// Mirrors the resource-chain predicates in Rules.py.
        /// </summary>
        public static bool IsTierUnlocked(int tier, HashSet<string> receivedItems)
            => IsTierUnlocked((ApTier)tier, receivedItems);

        public static bool IsTierUnlocked(ApTier tier, HashSet<string> receivedItems)
        {
            switch (tier)
            {
                case ApTier.Tier1:
                    return true;
                case ApTier.Tier2:
                    return receivedItems.Contains("Blueprint: Gear Workshop");
                case ApTier.Tier3:
                    return IsTierUnlocked(ApTier.Tier2, receivedItems)
                        && receivedItems.Contains("Blueprint: Scavenger Flag")
                        && receivedItems.Contains("Blueprint: Smelter");
                case ApTier.Tier4:
                    return IsTierUnlocked(ApTier.Tier3, receivedItems)
                        && receivedItems.Contains("Blueprint: Tapper's Shack")
                        && receivedItems.Contains("Blueprint: Wood Workshop");
                case ApTier.Tier5:
                    return IsTierUnlocked(ApTier.Tier4, receivedItems)
                        && receivedItems.Contains("Blueprint: Bot Part Factory")
                        && receivedItems.Contains("Blueprint: Bot Assembler");
                default:
                    return false;
            }
        }

        public static string GetTierRequirements(ApTier tier)
        {
            switch (tier)
            {
                case ApTier.Tier1: return "Always available";
                case ApTier.Tier2: return "Requires: Gear Workshop";
                case ApTier.Tier3: return "Requires: Scavenger Flag, Smelter";
                case ApTier.Tier4: return "Requires: Tapper's Shack, Wood Workshop";
                case ApTier.Tier5: return "Requires: Bot Part Factory, Bot Assembler";
                default: return "";
            }
        }

        // -----------------------------------------------------------------
        // Lookups
        // -----------------------------------------------------------------

        private static Dictionary<string, string> _itemNameToTemplate;

        public static bool TryGetLocationName(string templateName, out string locationName)
            => TemplateToLocation.TryGetValue(templateName, out locationName);

        /// <summary>
        /// Reverse lookup: AP item name ("Blueprint: Forester") → template name ("Forester.Folktails").
        /// </summary>
        public static bool TryGetTemplateName(string itemName, out string templateName)
        {
            if (_itemNameToTemplate == null)
            {
                _itemNameToTemplate = new Dictionary<string, string>();
                foreach (var kvp in TemplateToLocation)
                {
                    var buildingName = kvp.Value.Replace("Science: ", "");
                    _itemNameToTemplate[$"Blueprint: {buildingName}"] = kvp.Key;
                }
            }
            return _itemNameToTemplate.TryGetValue(itemName, out templateName);
        }

        public static IEnumerable<KeyValuePair<string, string>> AllEntries
            => TemplateToLocation;
    }
}
