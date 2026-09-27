using System;
using Timberborn.HazardousWeatherSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Counts droughts and badtides the colony survived, in the save data
    /// (SurvivedDroughts / SurvivedBadtides). A hazard counts on
    /// HazardousWeatherEndedEvent, which the game posts at the end of each cycle
    /// with a hazard (WeatherService.OnCycleEndedEvent -> EndHazardousWeather).
    /// Counting only runs for a save bound to an Archipelago slot, also while
    /// disconnected. See HazardSurvival for the rules.
    /// </summary>
    public class HazardSurvivalTracker : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly EventBus _eventBus;
        private readonly HazardousWeatherHistory _weatherHistory;
        private readonly HazardousWeatherService _hazardousWeatherService;
        private readonly ArchipelagoSaveData _saveData;

        public HazardSurvivalTracker(EventBus eventBus, HazardousWeatherHistory weatherHistory,
                                     HazardousWeatherService hazardousWeatherService,
                                     ArchipelagoSaveData saveData)
        {
            _eventBus = eventBus;
            _weatherHistory = weatherHistory;
            _hazardousWeatherService = hazardousWeatherService;
            _saveData = saveData;
        }

        public void Load()
        {
            _eventBus.Register(this);
            EnsureInitialized();
        }

        public void Unload()
        {
            try { _eventBus.Unregister(this); }
            catch { /* already torn down */ }
        }

        public int SurvivedDroughts
        {
            get { EnsureInitialized(); return HazardSurvival.Survived(_saveData.SurvivedDroughts); }
        }

        public int SurvivedBadtides
        {
            get { EnsureInitialized(); return HazardSurvival.Survived(_saveData.SurvivedBadtides); }
        }

        [OnEvent]
        public void OnHazardousWeatherEnded(HazardousWeatherEndedEvent e)
        {
            EnsureInitialized();
            var id = e?.HazardousWeather?.Id;
            if (HazardSurvival.IsDrought(id))
                _saveData.SurvivedDroughts = HazardSurvival.AfterEnded(_saveData.SurvivedDroughts);
            else if (HazardSurvival.IsBadtide(id))
                _saveData.SurvivedBadtides = HazardSurvival.AfterEnded(_saveData.SurvivedBadtides);
            Debug.Log($"[Archipelago] Hazard ended: {id ?? "(none)"}; survived droughts={_saveData.SurvivedDroughts} " +
                      $"badtides={_saveData.SurvivedBadtides} (-1 = save not bound to a slot)");
        }

        /// <summary>Sets the starting counts once the save is bound to a slot.</summary>
        private void EnsureInitialized()
        {
            if (_saveData.SurvivedDroughts >= 0 && _saveData.SurvivedBadtides >= 0) return;
            bool bound = !string.IsNullOrEmpty(_saveData.SavedSlot);
            if (!bound) return;
            string current = null;
            try { current = _hazardousWeatherService.CurrentCycleHazardousWeather?.Id; }
            catch (Exception ex) { Debug.LogWarning($"[Archipelago] Current hazard unknown: {ex.Message}"); }
            if (_saveData.SurvivedDroughts < 0)
                _saveData.SurvivedDroughts = HazardSurvival.InitialCount(true, _saveData.BaselineDroughtCount,
                    SafeCount(HazardSurvival.Drought), HazardSurvival.IsDrought(current));
            if (_saveData.SurvivedBadtides < 0)
                _saveData.SurvivedBadtides = HazardSurvival.InitialCount(true, _saveData.BaselineBadtideCount,
                    SafeCount(HazardSurvival.Badtide), HazardSurvival.IsBadtide(current));
            Debug.Log($"[Archipelago] Survival counts start at droughts={_saveData.SurvivedDroughts} " +
                      $"badtides={_saveData.SurvivedBadtides} (baselines {_saveData.BaselineDroughtCount}/" +
                      $"{_saveData.BaselineBadtideCount}, current hazard {current ?? "(none)"})");
        }

        private int SafeCount(string id)
        {
            try { return _weatherHistory.GetCyclesCount(id); }
            catch { return 0; }
        }
    }
}
