using System.Collections.Generic;
using System.Linq;

namespace ArchipelagoIntegration
{
    public enum ApTier { Tier1 = 1, Tier2, Tier3, Tier4, Tier5 }

    /// <summary>
    /// Maps Timberborn blueprint TemplateName values to Archipelago building names,
    /// with tier assignments matching Rules.py for the AP Shop progression.
    /// Supports both Folktails and Iron Teeth factions.
    /// </summary>
    internal static class ApBuildingLocations
    {
        // =================================================================
        // Template-to-building-name mapping (both factions)
        //
        // Key   = TemplateName (e.g. "Forester.Folktails")
        // Value = building display name (e.g. "Forester")
        // =================================================================

        private static readonly Dictionary<string, string> TemplateToBuilding = new()
        {
            // ----- Folktails entries (128 = 86 shared + 42 FT-only) -----
            { "Agora.Folktails", "Agora" },
            { "AquaticFarmhouse.Folktails", "Aquatic Farmhouse" },
            { "AquiferDrill.Folktails", "Aquifer Drill" },
            { "BadwaterDome.Folktails", "Badwater Dome" },
            { "BadwaterPump.Folktails", "Badwater Pump" },
            { "BadwaterRig.Folktails", "Badwater Rig" },
            { "Bakery.Folktails", "Bakery" },
            { "BeaverStatue.Folktails", "Beaver Statue" },
            { "Beehive.Folktails", "Beehive" },
            { "Bench.Folktails", "Bench" },
            { "BotAssembler.Folktails", "Bot Assembler" },
            { "BotPartFactory.Folktails", "Bot Part Factory" },
            { "BrazierOfBonding.Folktails", "Brazier of Bonding" },
            { "BuildersHut.Folktails", "Builders' Hut" },
            { "BulletinPole.Folktails", "Bulletin Pole" },
            { "Carousel.Folktails", "Carousel" },
            { "Centrifuge.Folktails", "Centrifuge" },
            { "Chronometer.Folktails", "Chronometer" },
            { "Clutch.Folktails", "Clutch" },
            { "ContaminationBarrier.Folktails", "Contamination Barrier" },
            { "ContaminationSensor.Folktails", "Contamination Sensor" },
            { "ContemplationSpot.Folktails", "Contemplation Spot" },
            { "DanceHall.Folktails", "Dance Hall" },
            { "DepthSensor.Folktails", "Depth Sensor" },
            { "Detailer.Folktails", "Detailer" },
            { "Detonator.Folktails", "Detonator" },
            { "DirtExcavator.Folktails", "Dirt Excavator" },
            { "DistrictCrossing.Folktails", "District Crossing" },
            { "DoubleDynamite.Folktails", "Double Dynamite" },
            { "DoubleFloodgate.Folktails", "Double Floodgate" },
            { "DoubleLodge.Folktails", "Double Lodge" },
            { "DoublePlatform.Folktails", "Double Platform" },
            { "Dynamite.Folktails", "Dynamite" },
            { "EarthRecultivator.Folktails", "Earth Recultivator" },
            { "ExplosivesFactory.Folktails", "Explosives Factory" },
            { "FarmerMonument.Folktails", "Farmer Monument" },
            { "FillValve.Folktails", "Fill Valve" },
            { "FireworkLauncher.Folktails", "Firework Launcher" },
            { "Floodgate.Folktails", "Floodgate" },
            { "FlowSensor.Folktails", "Flow Sensor" },
            { "FluidDump.Folktails", "Fluid Dump" },
            { "Forester.Folktails", "Forester" },
            { "FountainOfJoy.Folktails", "Fountain of Joy" },
            { "Gate.Folktails", "Gate" },
            { "GearWorkshop.Folktails", "Gear Workshop" },
            { "GeothermalEngine.Folktails", "Geothermal Engine" },
            { "GravityBattery.Folktails", "Gravity Battery" },
            { "Gristmill.Folktails", "Gristmill" },
            { "Hammock.Folktails", "Hammock" },
            { "Hedge.Folktails", "Hedge" },
            { "Herbalist.Folktails", "Herbalist" },
            { "HttpAdapter.Folktails", "HTTP Adapter" },
            { "HttpLever.Folktails", "HTTP Lever" },
            { "ImpermeableFloor.Folktails", "Impermeable Floor" },
            { "Indicator.Folktails", "Indicator" },
            { "Lantern.Folktails", "Lantern" },
            { "LargeTank.Folktails", "Large Tank" },
            { "LargeWarehouse.Folktails", "Large Warehouse" },
            { "LargeWaterPump.Folktails", "Large Water Pump" },
            { "LargeWindTurbine.Folktails", "Large Wind Turbine" },
            { "Levee.Folktails", "Levee" },
            { "Lever.Folktails", "Lever" },
            { "Lido.Folktails", "Lido" },
            { "MechanicalFluidPump.Folktails", "Mechanical Fluid Pump" },
            { "MedicalBed.Folktails", "Medical Bed" },
            { "MediumTank.Folktails", "Medium Tank" },
            { "Memory.Folktails", "Memory" },
            { "MetalPlatform3x3.Folktails", "Metal Platform 3x3" },
            { "MetalPlatform5x5.Folktails", "Metal Platform 5x5" },
            { "Mine.Folktails", "Mine" },
            { "MiniLodge.Folktails", "Mini Lodge" },
            { "MudPit.Folktails", "Mud Pit" },
            { "Observatory.Folktails", "Observatory" },
            { "Overhang2x1.Folktails", "Overhang 2x1" },
            { "Overhang3x1.Folktails", "Overhang 3x1" },
            { "Overhang4x1.Folktails", "Overhang 4x1" },
            { "Overhang5x1.Folktails", "Overhang 5x1" },
            { "Overhang6x1.Folktails", "Overhang 6x1" },
            { "PaperMill.Folktails", "Paper Mill" },
            { "Platform.Folktails", "Platform" },
            { "PoleBanner.Folktails", "Pole Banner" },
            { "PopulationCounter.Folktails", "Population Counter" },
            { "PowerMeter.Folktails", "Power Meter" },
            { "PrintingPress.Folktails", "Printing Press" },
            { "Refinery.Folktails", "Refinery" },
            { "Relay.Folktails", "Relay" },
            { "ResourceCounter.Folktails", "Resource Counter" },
            { "Roof1x1.Folktails", "Roof 1x1" },
            { "Roof1x2.Folktails", "Roof 1x2" },
            { "Roof2x2.Folktails", "Roof 2x2" },
            { "Roof2x3.Folktails", "Roof 2x3" },
            { "Roof3x2.Folktails", "Roof 3x2" },
            { "Scarecrow.Folktails", "Scarecrow" },
            { "ScavengerFlag.Folktails", "Scavenger Flag" },
            { "ScienceCounter.Folktails", "Science Counter" },
            { "Shower.Folktails", "Shower" },
            { "Sluice.Folktails", "Sluice" },
            { "Smelter.Folktails", "Smelter" },
            { "Speaker.Folktails", "Speaker" },
            { "SpiralStairs.Folktails", "Spiral Stairs" },
            { "SquareBanner.Folktails", "Square Banner" },
            { "Stairs.Folktails", "Stairs" },
            { "StreamGauge.Folktails", "Stream Gauge" },
            { "SuspensionBridge1x1.Folktails", "Suspension Bridge 1x1" },
            { "SuspensionBridge2x1.Folktails", "Suspension Bridge 2x1" },
            { "SuspensionBridge3x1.Folktails", "Suspension Bridge 3x1" },
            { "SuspensionBridge4x1.Folktails", "Suspension Bridge 4x1" },
            { "SuspensionBridge5x1.Folktails", "Suspension Bridge 5x1" },
            { "SuspensionBridge6x1.Folktails", "Suspension Bridge 6x1" },
            { "TappersShack.Folktails", "Tapper's Shack" },
            { "TerrainBlock.Folktails", "Terrain Block" },
            { "Timer.Folktails", "Timer" },
            { "TripleDynamite.Folktails", "Triple Dynamite" },
            { "TripleFloodgate.Folktails", "Triple Floodgate" },
            { "TripleLodge.Folktails", "Triple Lodge" },
            { "TriplePlatform.Folktails", "Triple Platform" },
            { "Tunnel.Folktails", "Tunnel" },
            { "UndergroundPile.Folktails", "Underground Pile" },
            { "Valve.Folktails", "Valve" },
            { "VerticalPowerShaft.Folktails", "Vertical Power Shaft" },
            { "WeatherStation.Folktails", "Weather Station" },
            { "Weathervane.Folktails", "Weathervane" },
            { "WindTurbine.Folktails", "Wind Turbine" },
            { "WoodFence.Folktails", "Wood Fence" },
            { "WoodWorkshop.Folktails", "Wood Workshop" },
            { "ZiplineBeam.Folktails", "Zipline Beam" },
            { "ZiplinePylon.Folktails", "Zipline Pylon" },
            { "ZiplineStation.Folktails", "Zipline Station" },

            // ----- Iron Teeth: shared buildings (86) -----
            { "AquiferDrill.IronTeeth", "Aquifer Drill" },
            { "BeaverStatue.IronTeeth", "Beaver Statue" },
            { "Bench.IronTeeth", "Bench" },
            { "BotAssembler.IronTeeth", "Bot Assembler" },
            { "BotPartFactory.IronTeeth", "Bot Part Factory" },
            { "BuildersHut.IronTeeth", "Builders' Hut" },
            { "Centrifuge.IronTeeth", "Centrifuge" },
            { "Chronometer.IronTeeth", "Chronometer" },
            { "Clutch.IronTeeth", "Clutch" },
            { "ContaminationSensor.IronTeeth", "Contamination Sensor" },
            { "DepthSensor.IronTeeth", "Depth Sensor" },
            { "Detailer.IronTeeth", "Detailer" },
            { "Detonator.IronTeeth", "Detonator" },
            { "DirtExcavator.IronTeeth", "Dirt Excavator" },
            { "DistrictCrossing.IronTeeth", "District Crossing" },
            { "DoubleDynamite.IronTeeth", "Double Dynamite" },
            { "DoubleFloodgate.IronTeeth", "Double Floodgate" },
            { "DoublePlatform.IronTeeth", "Double Platform" },
            { "Dynamite.IronTeeth", "Dynamite" },
            { "ExplosivesFactory.IronTeeth", "Explosives Factory" },
            { "FillValve.IronTeeth", "Fill Valve" },
            { "FireworkLauncher.IronTeeth", "Firework Launcher" },
            { "Floodgate.IronTeeth", "Floodgate" },
            { "FlowSensor.IronTeeth", "Flow Sensor" },
            { "FluidDump.IronTeeth", "Fluid Dump" },
            { "Forester.IronTeeth", "Forester" },
            { "Gate.IronTeeth", "Gate" },
            { "GearWorkshop.IronTeeth", "Gear Workshop" },
            { "GeothermalEngine.IronTeeth", "Geothermal Engine" },
            { "GravityBattery.IronTeeth", "Gravity Battery" },
            { "HttpAdapter.IronTeeth", "HTTP Adapter" },
            { "HttpLever.IronTeeth", "HTTP Lever" },
            { "ImpermeableFloor.IronTeeth", "Impermeable Floor" },
            { "Indicator.IronTeeth", "Indicator" },
            { "Lantern.IronTeeth", "Lantern" },
            { "LargeTank.IronTeeth", "Large Tank" },
            { "LargeWarehouse.IronTeeth", "Large Warehouse" },
            { "Levee.IronTeeth", "Levee" },
            { "Lever.IronTeeth", "Lever" },
            { "MedicalBed.IronTeeth", "Medical Bed" },
            { "MediumTank.IronTeeth", "Medium Tank" },
            { "Memory.IronTeeth", "Memory" },
            { "MetalPlatform3x3.IronTeeth", "Metal Platform 3x3" },
            { "MetalPlatform5x5.IronTeeth", "Metal Platform 5x5" },
            { "Overhang2x1.IronTeeth", "Overhang 2x1" },
            { "Overhang3x1.IronTeeth", "Overhang 3x1" },
            { "Overhang4x1.IronTeeth", "Overhang 4x1" },
            { "Overhang5x1.IronTeeth", "Overhang 5x1" },
            { "Overhang6x1.IronTeeth", "Overhang 6x1" },
            { "Platform.IronTeeth", "Platform" },
            { "PoleBanner.IronTeeth", "Pole Banner" },
            { "PopulationCounter.IronTeeth", "Population Counter" },
            { "PowerMeter.IronTeeth", "Power Meter" },
            { "Relay.IronTeeth", "Relay" },
            { "ResourceCounter.IronTeeth", "Resource Counter" },
            { "Roof1x1.IronTeeth", "Roof 1x1" },
            { "Roof1x2.IronTeeth", "Roof 1x2" },
            { "Roof2x2.IronTeeth", "Roof 2x2" },
            { "Roof2x3.IronTeeth", "Roof 2x3" },
            { "Roof3x2.IronTeeth", "Roof 3x2" },
            { "ScienceCounter.IronTeeth", "Science Counter" },
            { "Sluice.IronTeeth", "Sluice" },
            { "Smelter.IronTeeth", "Smelter" },
            { "Speaker.IronTeeth", "Speaker" },
            { "SpiralStairs.IronTeeth", "Spiral Stairs" },
            { "SquareBanner.IronTeeth", "Square Banner" },
            { "Stairs.IronTeeth", "Stairs" },
            { "StreamGauge.IronTeeth", "Stream Gauge" },
            { "SuspensionBridge1x1.IronTeeth", "Suspension Bridge 1x1" },
            { "SuspensionBridge2x1.IronTeeth", "Suspension Bridge 2x1" },
            { "SuspensionBridge3x1.IronTeeth", "Suspension Bridge 3x1" },
            { "SuspensionBridge4x1.IronTeeth", "Suspension Bridge 4x1" },
            { "SuspensionBridge5x1.IronTeeth", "Suspension Bridge 5x1" },
            { "SuspensionBridge6x1.IronTeeth", "Suspension Bridge 6x1" },
            { "TappersShack.IronTeeth", "Tapper's Shack" },
            { "TerrainBlock.IronTeeth", "Terrain Block" },
            { "Timer.IronTeeth", "Timer" },
            { "TripleDynamite.IronTeeth", "Triple Dynamite" },
            { "TripleFloodgate.IronTeeth", "Triple Floodgate" },
            { "TriplePlatform.IronTeeth", "Triple Platform" },
            { "Tunnel.IronTeeth", "Tunnel" },
            { "Valve.IronTeeth", "Valve" },
            { "VerticalPowerShaft.IronTeeth", "Vertical Power Shaft" },
            { "WeatherStation.IronTeeth", "Weather Station" },
            { "WoodFence.IronTeeth", "Wood Fence" },
            { "WoodWorkshop.IronTeeth", "Wood Workshop" },

            // ----- Iron Teeth: IT-only buildings (40) -----
            { "AdvancedBreedingPod.IronTeeth", "Advanced Breeding Pod" },
            { "BadwaterDischarge.IronTeeth", "Badwater Discharge" },
            { "BeaverBust.IronTeeth", "Beaver Bust" },
            { "Bell.IronTeeth", "Bell" },
            { "Brazier.IronTeeth", "Brazier" },
            { "ChargingStation.IronTeeth", "Charging Station" },
            { "CoffeeBrewery.IronTeeth", "Coffee Brewery" },
            { "ControlTower.IronTeeth", "Control Tower" },
            { "DecontaminationPod.IronTeeth", "Decontamination Pod" },
            { "DecorativeClock.IronTeeth", "Decorative Clock" },
            { "DeepBadwaterPump.IronTeeth", "Deep Badwater Pump" },
            { "DeepMechanicalFluidPump.IronTeeth", "Deep Mechanical Fluid Pump" },
            { "DoubleShower.IronTeeth", "Double Shower" },
            { "EarthRepopulator.IronTeeth", "Earth Repopulator" },
            { "EfficientMine.IronTeeth", "Efficient Mine" },
            { "ExercisePlaza.IronTeeth", "Exercise Plaza" },
            { "FlameOfUnity.IronTeeth", "Flame of Unity" },
            { "FoodFactory.IronTeeth", "Food Factory" },
            { "GreaseFactory.IronTeeth", "Grease Factory" },
            { "HydroponicGarden.IronTeeth", "Hydroponic Garden" },
            { "IrrigationBarrier.IronTeeth", "Irrigation Barrier" },
            { "LaborerMonument.IronTeeth", "Laborer Monument" },
            { "LargeBarrack.IronTeeth", "Large Barrack" },
            { "LargeRowhouse.IronTeeth", "Large Rowhouse" },
            { "LargeWaterWheel.IronTeeth", "Large Water Wheel" },
            { "MetalFence.IronTeeth", "Metal Fence" },
            { "Metalsmith.IronTeeth", "Metalsmith" },
            { "Motivatorium.IronTeeth", "Motivatorium" },
            { "MudBath.IronTeeth", "Mud Bath" },
            { "Numbercruncher.IronTeeth", "Numbercruncher" },
            { "OilPress.IronTeeth", "Oil Press" },
            { "Rowhouse.IronTeeth", "Rowhouse" },
            { "Scratcher.IronTeeth", "Scratcher" },
            { "SteamEngine.IronTeeth", "Steam Engine" },
            { "SwimmingPool.IronTeeth", "Swimming Pool" },
            { "TributeToIngenuity.IronTeeth", "Tribute to Ingenuity" },
            { "Tubeway.IronTeeth", "Tubeway" },
            { "TubewayStation.IronTeeth", "Tubeway Station" },
            { "VerticalTubeway.IronTeeth", "Vertical Tubeway" },
            { "WindTunnel.IronTeeth", "Wind Tunnel" },
        };

        // =================================================================
        // Tier assignments — must match Rules.py tier predicates
        //
        // Shared buildings use the same tier for both factions.
        // IT-only buildings are added to appropriate tiers below.
        // =================================================================

        private static readonly HashSet<string> Tier1Buildings = new()
        {
            // Shared T1
            "Forester", "Medium Tank", "Levee", "Vertical Power Shaft",
            "Medical Bed", "Stairs", "Platform", "Builders' Hut", "Lever",
            "Roof 1x1", "Bench", "Roof 1x2", "Lantern", "Flow Sensor", "Relay",
            // FT-only T1
            "Mini Lodge", "Shower", "Contemplation Spot",
            // IT-only T1
            "Double Shower", "Scratcher", "Oil Press", "Brazier", "Rowhouse",
            "Beaver Bust", "Large Water Wheel",
        };

        private static readonly HashSet<string> Tier2Buildings = new()
        {
            // Shared T2
            "Gear Workshop", "Fill Valve", "Roof 2x2",
            "Geothermal Engine", "Floodgate", "Impermeable Floor",
            "Large Warehouse", "Fluid Dump", "Double Floodgate",
            "Suspension Bridge 1x1", "Double Platform", "Gate", "Triple Platform",
            "Suspension Bridge 2x1", "Roof 2x3", "Roof 3x2",
            "Stream Gauge", "Wood Fence", "Chronometer", "Depth Sensor",
            "Population Counter", "Resource Counter", "Science Counter",
            "Weather Station",
            // FT-only T2
            "Aquatic Farmhouse", "Bakery", "Gristmill", "Hammock",
            "Wind Turbine", "Double Lodge", "Badwater Pump",
            "Paper Mill", "Lido", "Herbalist", "Triple Lodge",
            "Hedge", "Scarecrow", "Weathervane",
            // IT-only T2
            "Hydroponic Garden", "Charging Station", "Swimming Pool",
            "Deep Badwater Pump", "Food Factory", "Large Barrack",
            "Steam Engine", "Exercise Plaza", "Coffee Brewery", "Bell", "Tubeway",
        };

        private static readonly HashSet<string> Tier3Buildings = new()
        {
            // Shared T3
            "Smelter", "Bot Part Factory", "Aquifer Drill",
            "Explosives Factory", "Valve", "Triple Floodgate",
            "Clutch", "Gravity Battery", "Tapper's Shack",
            "Suspension Bridge 3x1", "Overhang 2x1", "Spiral Stairs",
            "Contamination Sensor", "Indicator", "Speaker",
            "Detonator", "Overhang 3x1", "Suspension Bridge 4x1",
            "Power Meter", "Timer", "Firework Launcher", "Large Tank",
            "District Crossing", "Centrifuge", "Wood Workshop",
            "Square Banner",
            // FT-only T3
            "Scavenger Flag", "Printing Press", "Refinery",
            "Large Water Pump", "Contamination Barrier",
            "Agora", "Beehive", "Zipline Pylon",
            "Zipline Beam", "Zipline Station", "Carousel",
            "Bulletin Pole", "Beaver Statue", "Pole Banner",
            // IT-only T3
            "Metalsmith", "Metal Fence", "Decontamination Pod",
            "Irrigation Barrier", "Decorative Clock", "Large Rowhouse",
            "Vertical Tubeway", "Wind Tunnel", "Tubeway Station",
        };

        private static readonly HashSet<string> Tier4Buildings = new()
        {
            // Shared T4
            "Bot Assembler", "Dynamite", "Double Dynamite",
            "Terrain Block", "Triple Dynamite", "Dirt Excavator", "Tunnel",
            "Detailer", "Metal Platform 3x3", "Overhang 4x1",
            "Suspension Bridge 5x1", "Memory", "Metal Platform 5x5",
            "Overhang 5x1", "Suspension Bridge 6x1", "Overhang 6x1",
            // FT-only T4
            "Observatory", "Large Wind Turbine", "Dance Hall", "Mud Pit",
            "Underground Pile", "Mechanical Fluid Pump", "Badwater Dome",
            "Farmer Monument", "Brazier of Bonding", "Earth Recultivator",
            // IT-only T4
            "Advanced Breeding Pod", "Control Tower", "Laborer Monument",
            "Motivatorium", "Numbercruncher", "Mud Bath", "Grease Factory",
            "Deep Mechanical Fluid Pump", "Flame of Unity",
        };

        private static readonly HashSet<string> Tier5Buildings = new()
        {
            // Shared T5
            "HTTP Lever", "HTTP Adapter",
            // FT-only T5
            "Mine", "Badwater Rig", "Fountain of Joy",
            // IT-only T5
            "Badwater Discharge", "Efficient Mine",
            "Tribute to Ingenuity", "Earth Repopulator",
        };

        // -----------------------------------------------------------------
        // Tier gate predicates — check received AP items
        // Mirrors Rules.py tier helpers: _tier1 through _tier5
        // -----------------------------------------------------------------

        public static ApTier GetTier(string buildingName)
        {
            if (Tier1Buildings.Contains(buildingName)) return ApTier.Tier1;
            if (Tier2Buildings.Contains(buildingName)) return ApTier.Tier2;
            if (Tier3Buildings.Contains(buildingName)) return ApTier.Tier3;
            if (Tier4Buildings.Contains(buildingName)) return ApTier.Tier4;
            if (Tier5Buildings.Contains(buildingName)) return ApTier.Tier5;
            return ApTier.Tier1; // fallback
        }

        /// <summary>
        /// Returns true if the given tier is accessible based on received AP items.
        /// Mirrors the resource-chain predicates in Rules.py.
        /// </summary>
        public static bool IsTierUnlocked(int tier, HashSet<string> receivedItems,
                                          string faction = "Folktails")
            => IsTierUnlocked((ApTier)tier, receivedItems, faction);

        public static bool IsTierUnlocked(ApTier tier, HashSet<string> receivedItems,
                                          string faction = "Folktails")
        {
            switch (tier)
            {
                case ApTier.Tier1:
                    return true;
                case ApTier.Tier2:
                    return receivedItems.Contains("Blueprint: Gear Workshop");
                case ApTier.Tier3:
                    bool hasTier2 = IsTierUnlocked(ApTier.Tier2, receivedItems, faction);
                    bool hasSmelter = receivedItems.Contains("Blueprint: Smelter");
                    if (faction == "IronTeeth")
                        return hasTier2 && hasSmelter;
                    return hasTier2 && hasSmelter
                        && receivedItems.Contains("Blueprint: Scavenger Flag");
                case ApTier.Tier4:
                    return IsTierUnlocked(ApTier.Tier3, receivedItems, faction)
                        && receivedItems.Contains("Blueprint: Tapper's Shack")
                        && receivedItems.Contains("Blueprint: Wood Workshop");
                case ApTier.Tier5:
                    return IsTierUnlocked(ApTier.Tier4, receivedItems, faction)
                        && receivedItems.Contains("Blueprint: Bot Part Factory")
                        && receivedItems.Contains("Blueprint: Bot Assembler");
                default:
                    return false;
            }
        }

        // -----------------------------------------------------------------
        // Faction helper
        // -----------------------------------------------------------------

        /// <summary>
        /// Reads the faction string from slot data. Returns "Folktails" or "IronTeeth".
        /// </summary>
        public static string GetFaction()
        {
            if (ArchipelagoManager.SlotData != null
                && ArchipelagoManager.SlotData.TryGetValue("faction", out var factionObj))
            {
                return factionObj?.ToString() ?? "Folktails";
            }
            return "Folktails";
        }

        // -----------------------------------------------------------------
        // Lookups
        // -----------------------------------------------------------------

        private static Dictionary<string, string> _ftItemToTemplate;
        private static Dictionary<string, string> _itItemToTemplate;

        public static bool TryGetBuildingName(string templateName, out string buildingName)
            => TemplateToBuilding.TryGetValue(templateName, out buildingName);

        /// <summary>
        /// Backward compat alias for TryGetBuildingName.
        /// </summary>
        public static bool TryGetLocationName(string templateName, out string locationName)
            => TemplateToBuilding.TryGetValue(templateName, out locationName);

        /// <summary>
        /// Reverse lookup: AP item name ("Blueprint: Forester") to template name
        /// ("Forester.Folktails" or "Forester.IronTeeth" depending on faction).
        /// </summary>
        public static bool TryGetTemplateName(string itemName, out string templateName,
                                              string faction = null)
        {
            faction ??= GetFaction();
            bool isIT = faction == "IronTeeth";
            var cache = isIT ? _itItemToTemplate : _ftItemToTemplate;
            if (cache == null)
            {
                string suffix = isIT ? ".IronTeeth" : ".Folktails";
                cache = new Dictionary<string, string>();
                foreach (var kvp in TemplateToBuilding)
                {
                    if (!kvp.Key.EndsWith(suffix)) continue;
                    cache[$"Blueprint: {kvp.Value}"] = kvp.Key;
                }
                if (isIT) _itItemToTemplate = cache;
                else _ftItemToTemplate = cache;
            }
            return cache.TryGetValue(itemName, out templateName);
        }

        /// <summary>
        /// Returns all template entries for the given faction (or all if null).
        /// </summary>
        public static IEnumerable<KeyValuePair<string, string>> GetEntries(string faction = null)
        {
            if (faction == null)
                return TemplateToBuilding;

            string suffix = (faction == "IronTeeth") ? ".IronTeeth" : ".Folktails";
            return TemplateToBuilding.Where(kvp => kvp.Key.EndsWith(suffix));
        }

        /// <summary>
        /// All entries (both factions). Used by VanillaUnlockBlocker with faction filtering.
        /// </summary>
        public static IEnumerable<KeyValuePair<string, string>> AllEntries
            => TemplateToBuilding;

        /// <summary>
        /// Returns the tier requirement text for the given tier and faction.
        /// IT Tier 3 does not require Scavenger Flag (scrap gathering is free).
        /// </summary>
        public static string GetTierRequirementText(int tier, string faction = "Folktails")
        {
            switch (tier)
            {
                case 2: return "Requires: Gear Workshop";
                case 3:
                    return faction == "IronTeeth"
                        ? "Requires: Smelter"
                        : "Requires: Scavenger Flag + Smelter";
                case 4: return "Requires: Tapper's Shack + Wood Workshop";
                case 5: return "Requires: Bot Part Factory + Bot Assembler";
                default: return "";
            }
        }
    }
}
