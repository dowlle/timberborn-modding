using System;
using Timberborn.GameCycleSystem;
using Timberborn.HazardousWeatherSystem;
using Timberborn.SingletonSystem;
using Timberborn.WeatherSystem;
using UnityEngine;

namespace ArchipelagoIntegration
{
    /// <summary>
    /// Observes game cycle transitions and logs weather + history state at every
    /// CycleStarted, CycleEnded, and the two cycle-day-started boundaries we care
    /// about (day 1 and the natural hazardous-transition day).
    ///
    /// Critical for debugging trap-induced cycle issues — when an AP Hazardous
    /// Weather trap interrupts the natural cycle, the next CycleStarted event
    /// will show whether the temperate cycle resumed in a degraded state. The
    /// post-v0.0.4 water-spawn regression should be visible in the diff between
    /// pre-trap and post-trap CycleStarted log entries.
    ///
    /// All log lines use the [Archipelago/Cycle] prefix so they can be filtered
    /// out of the broader [Archipelago] log stream:
    ///   grep "Archipelago/Cycle" Player.log
    /// </summary>
    public class CycleEventObserver : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly EventBus _eventBus;
        private readonly GameCycleService _gameCycleService;
        private readonly WeatherService _weatherService;
        private readonly HazardousWeatherService _hazardousWeatherService;
        private readonly TemperateWeatherDurationService _temperateWeatherDurationService;
        private readonly HazardousWeatherHistory _weatherHistory;

        public CycleEventObserver(
            EventBus eventBus,
            GameCycleService gameCycleService,
            WeatherService weatherService,
            HazardousWeatherService hazardousWeatherService,
            TemperateWeatherDurationService temperateWeatherDurationService,
            HazardousWeatherHistory weatherHistory)
        {
            _eventBus = eventBus;
            _gameCycleService = gameCycleService;
            _weatherService = weatherService;
            _hazardousWeatherService = hazardousWeatherService;
            _temperateWeatherDurationService = temperateWeatherDurationService;
            _weatherHistory = weatherHistory;
        }

        public void Load()
        {
            _eventBus.Register(this);
            // Initial snapshot — useful baseline for fresh starts and save reloads
            // because the very first CycleStartedEvent may have already fired by
            // the time this singleton's Load() runs.
            Debug.Log($"[Archipelago/Cycle] Observer registered. Initial: " +
                      $"cycle={_gameCycleService.Cycle}, day={_gameCycleService.CycleDay}, " +
                      $"IsHazardous={_weatherService.IsHazardousWeather}, " +
                      $"TempDur={_temperateWeatherDurationService.TemperateWeatherDuration}, " +
                      $"HazDur={_hazardousWeatherService.HazardousWeatherDuration}, " +
                      $"HazStartDay={_weatherService.HazardousWeatherStartCycleDay}, " +
                      $"HazType={_hazardousWeatherService.CurrentCycleHazardousWeather?.GetType().Name ?? "(null)"}, " +
                      $"droughtsTotal={SafeCount("DroughtWeather")}, " +
                      $"badtidesTotal={SafeCount("BadtideWeather")}");
        }

        public void Unload()
        {
            try { _eventBus.Unregister(this); }
            catch { /* unregister may throw if never registered or already torn down */ }
        }

        [OnEvent]
        public void OnCycleStarted(CycleStartedEvent e)
        {
            // CycleStarted fires once per cycle. The state captured here is the
            // freshly-rolled cycle setup (TemperateDuration regenerated,
            // HazardousWeatherRandomizer pre-rolled drought/badtide pick, both
            // durations set). This is the line to compare across cycles when
            // hunting for state regressions caused by AP traps.
            Debug.Log($"[Archipelago/Cycle] CycleStarted: " +
                      $"cycle={_gameCycleService.Cycle}, day={_gameCycleService.CycleDay}, " +
                      $"TempDur={_temperateWeatherDurationService.TemperateWeatherDuration}, " +
                      $"HazDur={_hazardousWeatherService.HazardousWeatherDuration}, " +
                      $"HazStartDay={_weatherService.HazardousWeatherStartCycleDay}, " +
                      $"HazType={_hazardousWeatherService.CurrentCycleHazardousWeather?.GetType().Name ?? "(null)"}, " +
                      $"droughtsTotal={SafeCount("DroughtWeather")}, " +
                      $"badtidesTotal={SafeCount("BadtideWeather")}");
        }

        [OnEvent]
        public void OnCycleEnded(CycleEndedEvent e)
        {
            Debug.Log($"[Archipelago/Cycle] CycleEnded: " +
                      $"cycle={_gameCycleService.Cycle}, day={_gameCycleService.CycleDay}, " +
                      $"IsHazardous={_weatherService.IsHazardousWeather}, " +
                      $"droughtsTotal={SafeCount("DroughtWeather")}, " +
                      $"badtidesTotal={SafeCount("BadtideWeather")}");
        }

        [OnEvent]
        public void OnCycleDayStarted(CycleDayStartedEvent e)
        {
            // Per-day events fire daily — too noisy to log every one. Limit to
            // phase-boundary days: day 1 (temperate phase begins) and the natural
            // hazardous transition day. AP-trap-driven hazardous starts are logged
            // by ApEffectHandler.ScheduleHazardousWeather, not here.
            int day = _gameCycleService.CycleDay;
            int hazStartDay = _weatherService.HazardousWeatherStartCycleDay;

            if (day == 1)
            {
                Debug.Log($"[Archipelago/Cycle] DayStarted day=1 (Temperate phase begins) of " +
                          $"cycle {_gameCycleService.Cycle}. " +
                          $"Will transition to hazardous on day {hazStartDay} " +
                          $"(type pre-rolled: {_hazardousWeatherService.CurrentCycleHazardousWeather?.GetType().Name ?? "(null)"}).");
            }
            else if (day == hazStartDay)
            {
                Debug.Log($"[Archipelago/Cycle] DayStarted day={day} (natural hazardous transition day) " +
                          $"of cycle {_gameCycleService.Cycle}. " +
                          $"IsHazardous={_weatherService.IsHazardousWeather}, " +
                          $"HazType={_hazardousWeatherService.CurrentCycleHazardousWeather?.GetType().Name ?? "(null)"}, " +
                          $"HazDur={_hazardousWeatherService.HazardousWeatherDuration}.");
            }
        }

        /// <summary>
        /// Wrap GetCyclesCount in a try/catch so a single failed lookup doesn't
        /// abort the whole observer log line.
        /// </summary>
        private int SafeCount(string hazardousId)
        {
            try { return _weatherHistory.GetCyclesCount(hazardousId); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Archipelago/Cycle] GetCyclesCount({hazardousId}) failed: {ex.Message}");
                return -1;
            }
        }
    }
}
