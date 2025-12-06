using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Validations;
using Fluxa.NINA.HorizonTarget.Models;
using Fluxa.NINA.HorizonTarget.Services;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Astrometry;

namespace Fluxa.NINA.HorizonTarget.HorizontargetSequenceItems {
    [ExportMetadata("Name", "Track Moving Target Trigger")]
    [ExportMetadata("Description", "Automatically checks drift and slews telescope before each instruction to track moving target")]
    [ExportMetadata("Icon", "HorizonTargetSVG")]
    [ExportMetadata("Category", "Horizon Target")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TrackMovingTargetTrigger : SequenceTrigger, IValidatable {
        
        private readonly ITelescopeMediator telescopeMediator;
        private readonly EphemerisInterpolationService interpolationService;

        [ImportingConstructor]
        public TrackMovingTargetTrigger(ITelescopeMediator telescopeMediator) {
            this.telescopeMediator = telescopeMediator;
            this.interpolationService = new EphemerisInterpolationService();

            // Set defaults
            DriftThresholdArcsec = 30.0;

            // Subscribe to ephemeris cache updates
            FetchEphemerisInstruction.EphemerisCacheUpdated += OnEphemerisCacheUpdated;
        }

        // Copy constructor for cloning
        private TrackMovingTargetTrigger(TrackMovingTargetTrigger copyMe) : this(copyMe.telescopeMediator) {
            CopyMetaData(copyMe);
            DriftThresholdArcsec = copyMe.DriftThresholdArcsec;
        }

        public override object Clone() {
            return new TrackMovingTargetTrigger(this) {
                DriftThresholdArcsec = DriftThresholdArcsec
            };
        }

        private void OnEphemerisCacheUpdated(object sender, EventArgs e) {
            // Notify UI that HasEphemerisData and CachedTargetName may have changed
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher.CheckAccess()) {
                RaisePropertyChanged(nameof(HasEphemerisData));
                RaisePropertyChanged(nameof(CachedTargetName));
            } else {
                dispatcher.BeginInvoke(new Action(() => {
                    RaisePropertyChanged(nameof(HasEphemerisData));
                    RaisePropertyChanged(nameof(CachedTargetName));
                }));
            }
        }

        #region Properties

        private double driftThresholdArcsec;
        [JsonProperty]
        public double DriftThresholdArcsec {
            get => driftThresholdArcsec;
            set {
                driftThresholdArcsec = Math.Max(0.1, Math.Min(3600.0, value));
                RaisePropertyChanged();
            }
        }

        public bool HasEphemerisData {
            get {
                return FetchEphemerisInstruction.CachedEphemeris != null && 
                       FetchEphemerisInstruction.CachedEphemeris.Count > 0;
            }
        }

        public string CachedTargetName {
            get => FetchEphemerisInstruction.CachedTargetName;
        }

        #endregion

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            // Trigger should run if:
            // 1. Ephemeris data is available
            // 2. Telescope is connected
            return HasEphemerisData && 
                   telescopeMediator.GetInfo().Connected;
        }

        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                global::NINA.Core.Utility.Logger.Debug("[HorizonTarget] TrackMovingTargetTrigger: Execute method started");
                
                if (!ShouldTrigger(null, null)) {
                    global::NINA.Core.Utility.Logger.Debug("[HorizonTarget] TrackMovingTargetTrigger: ShouldTrigger returned false, skipping");
                    return;
                }

                await CheckAndSlewIfNeeded(progress, token);
                
                global::NINA.Core.Utility.Logger.Debug("[HorizonTarget] TrackMovingTargetTrigger: Execute method completed successfully");
            } catch (OperationCanceledException) {
                global::NINA.Core.Utility.Logger.Info("[HorizonTarget] TrackMovingTargetTrigger: Operation was cancelled");
                throw; // Re-throw cancellation to allow sequence to stop properly
            } catch (Exception ex) {
                global::NINA.Core.Utility.Logger.Error($"[HorizonTarget] TrackMovingTargetTrigger: Execute method error: {ex}");
                // Don't re-throw - allow sequence to continue even if trigger fails
            }
        }

        private async Task CheckAndSlewIfNeeded(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                var currentTime = DateTime.UtcNow;
                var ephemeris = FetchEphemerisInstruction.CachedEphemeris;
                if (ephemeris == null || ephemeris.Count == 0) {
                    global::NINA.Core.Utility.Logger.Debug("[HorizonTarget] TrackMovingTargetTrigger: No ephemeris data available");
                    return;
                }

                var targetPosition = interpolationService.InterpolatePosition(currentTime, ephemeris);
                
                // Log current time and interpolated position to verify time-based interpolation
                global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] TrackMovingTargetTrigger: Interpolating at UTC {currentTime:yyyy-MM-dd HH:mm:ss} -> Target position: RA: {targetPosition.RAString} Dec: {targetPosition.DecString}");
                
                var telescopeInfo = telescopeMediator.GetInfo();
                if (!telescopeInfo.Connected || telescopeInfo.Coordinates == null) {
                    global::NINA.Core.Utility.Logger.Debug("[HorizonTarget] TrackMovingTargetTrigger: Telescope not connected or no coordinates");
                    return;
                }

                var distanceArcsec = interpolationService.GetAngularDistanceArcsec(
                    telescopeInfo.Coordinates,
                    targetPosition
                );

                global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] TrackMovingTargetTrigger: Current drift: {distanceArcsec:F2} arcsec (threshold: {DriftThresholdArcsec:F2} arcsec)");

                if (distanceArcsec > DriftThresholdArcsec) {
                    progress?.Report(new ApplicationStatus {
                        Status = $"Trigger: Slewing to target (drift: {distanceArcsec:F1}\")..."
                    });

                    global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] TrackMovingTargetTrigger: Drift {distanceArcsec:F2}\" exceeds threshold {DriftThresholdArcsec:F2}\". Starting slew to target position.");

                    try {
                        await telescopeMediator.SlewToCoordinatesAsync(targetPosition, token);
                        
                        global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] TrackMovingTargetTrigger: Slew operation completed successfully.");

                        progress?.Report(new ApplicationStatus {
                            Status = $"Trigger: Slew complete. Target at RA: {targetPosition.RAString} Dec: {targetPosition.DecString}"
                        });
                    } catch (OperationCanceledException) {
                        global::NINA.Core.Utility.Logger.Info("[HorizonTarget] TrackMovingTargetTrigger: Slew operation was cancelled");
                        throw; // Re-throw to propagate cancellation
                    } catch (Exception ex) {
                        global::NINA.Core.Utility.Logger.Error($"[HorizonTarget] TrackMovingTargetTrigger: Slew operation failed: {ex}");
                        // Don't re-throw - allow sequence to continue
                    }
                } else {
                    global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] TrackMovingTargetTrigger: Drift {distanceArcsec:F2}\" within threshold. No slew needed.");
                }
            } catch (OperationCanceledException) {
                throw; // Re-throw cancellation
            } catch (Exception ex) {
                global::NINA.Core.Utility.Logger.Error($"[HorizonTarget] TrackMovingTargetTrigger: CheckAndSlewIfNeeded error: {ex}");
                // Don't re-throw - allow sequence to continue
            }
        }

        public IList<string> Issues { get; set; } = new List<string>();

        public bool Validate() {
            var issuesList = new List<string>();
            if (!HasEphemerisData) {
                issuesList.Add("⚠ No ephemeris data. Add 'Fetch Ephemeris from HORIZONS' instruction first.");
            }
            if (!telescopeMediator.GetInfo().Connected) {
                issuesList.Add("Telescope is not connected.");
            }
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher.CheckAccess()) {
                Issues.Clear();
                foreach (var issue in issuesList) Issues.Add(issue);
            } else {
                dispatcher.Invoke(() => {
                    Issues.Clear();
                    foreach (var issue in issuesList) Issues.Add(issue);
                });
            }
            return issuesList.Count == 0;
        }

        public override string ToString() {
            var status = HasEphemerisData ? "Ready" : "⚠ No Ephemeris";
            return $"Track Moving Target Trigger: {CachedTargetName ?? "Unknown"} ({status}, Threshold: {DriftThresholdArcsec:F1}\")";
        }
    }
}
