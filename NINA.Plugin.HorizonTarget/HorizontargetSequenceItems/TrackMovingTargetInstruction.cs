using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using Fluxa.NINA.HorizonTarget.Models;
using Fluxa.NINA.HorizonTarget.Services;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Astrometry;

namespace Fluxa.NINA.HorizonTarget.HorizontargetSequenceItems {
    [ExportMetadata("Name", "Track Moving Target")]
    [ExportMetadata("Description", "Slews telescope to track moving target (comet/asteroid) based on cached ephemeris data. Use the trigger for automatic drift-based tracking.")]
    [ExportMetadata("Icon", "HorizonTargetSVG")]
    [ExportMetadata("Category", "Horizon Target")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TrackMovingTargetInstruction : SequenceItem, IValidatable {
        
        private readonly ITelescopeMediator telescopeMediator;
        private readonly EphemerisInterpolationService interpolationService;

        [ImportingConstructor]
        public TrackMovingTargetInstruction(ITelescopeMediator telescopeMediator) {
            this.telescopeMediator = telescopeMediator;
            this.interpolationService = new EphemerisInterpolationService();

            // Subscribe to ephemeris cache updates
            FetchEphemerisInstruction.EphemerisCacheUpdated += OnEphemerisCacheUpdated;
        }

        // Copy constructor for cloning
        private TrackMovingTargetInstruction(TrackMovingTargetInstruction copyMe) : this(copyMe.telescopeMediator) {
            CopyMetaData(copyMe);
        }

        public override object Clone() {
            return new TrackMovingTargetInstruction(this);
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

        private Coordinates currentTargetPosition;
        public Coordinates CurrentTargetPosition {
            get => currentTargetPosition;
            private set {
                currentTargetPosition = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Indicates whether ephemeris data is available (for UI binding)
        /// </summary>
        public bool HasEphemerisData {
            get {
                return FetchEphemerisInstruction.CachedEphemeris != null && 
                       FetchEphemerisInstruction.CachedEphemeris.Count > 0;
            }
        }

        /// <summary>
        /// Gets the cached target name (for display)
        /// </summary>
        public string CachedTargetName {
            get => FetchEphemerisInstruction.CachedTargetName;
        }

        #endregion

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            // Check if ephemeris data is available
            if (!HasEphemerisData) {
                throw new SequenceEntityFailedException("No ephemeris data available. Please run 'Fetch Ephemeris from HORIZONS' instruction first.");
            }

            // Check telescope connection
            if (!telescopeMediator.GetInfo().Connected) {
                throw new SequenceEntityFailedException("Telescope is not connected. Cannot track moving target.");
            }

            await CheckAndSlewIfNeeded(progress, token);
        }

        private async Task CheckAndSlewIfNeeded(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                // Get current time
                var currentTime = DateTime.UtcNow;

                // Get cached ephemeris
                var ephemeris = FetchEphemerisInstruction.CachedEphemeris;
                if (ephemeris == null || ephemeris.Count == 0) {
                    throw new SequenceEntityFailedException("Ephemeris data is empty. Please run 'Fetch Ephemeris from HORIZONS' instruction first.");
                }

                // Interpolate target position
                var targetPosition = interpolationService.InterpolatePosition(
                    currentTime,
                    ephemeris
                );

                CurrentTargetPosition = targetPosition;

                // Get current telescope position
                var telescopeInfo = telescopeMediator.GetInfo();
                if (!telescopeInfo.Connected) {
                    global::NINA.Core.Utility.Logger.Warning("[HorizonTarget] Telescope disconnected during position check");
                    return;
                }

                var currentTelescopePosition = telescopeInfo.Coordinates;
                if (currentTelescopePosition == null) {
                    global::NINA.Core.Utility.Logger.Warning("[HorizonTarget] Could not get current telescope position");
                    return;
                }

                // Calculate angular distance for logging
                var distanceArcsec = interpolationService.GetAngularDistanceArcsec(
                    currentTelescopePosition,
                    targetPosition
                );

                global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] Current drift: {distanceArcsec:F2} arcsec. Slewing to target position.");

                // Always slew to target position (no threshold check - that's handled by the trigger)
                progress?.Report(new ApplicationStatus {
                    Status = $"Slewing to target position (drift: {distanceArcsec:F1}\")..."
                });

                global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] Slewing to target position. Current drift: {distanceArcsec:F2}\".");

                // Slew to target position
                await telescopeMediator.SlewToCoordinatesAsync(targetPosition, token);

                progress?.Report(new ApplicationStatus {
                    Status = $"Slew complete. Tracking target at RA: {targetPosition.RAString} Dec: {targetPosition.DecString}"
                });
            } catch (Exception ex) {
                global::NINA.Core.Utility.Logger.Error($"[HorizonTarget] Error during position check/slew: {ex}");
                throw new SequenceEntityFailedException($"Failed to track moving target: {ex.Message}", ex);
            }
        }

        public IList<string> Issues { get; set; } = new List<string>();

        public bool Validate() {
            // Collect issues in a temporary list first
            var issuesList = new List<string>();

            // Check if ephemeris data is available
            if (!HasEphemerisData) {
                issuesList.Add("⚠ REQUIRED: No ephemeris data available. Add 'Fetch Ephemeris from HORIZONS' instruction before this one.");
            }

            // Check telescope connection
            if (!telescopeMediator.GetInfo().Connected) {
                issuesList.Add("Telescope is not connected. Connect telescope in Equipment tab.");
            }

            // Update Issues collection on UI thread
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher.CheckAccess()) {
                // Already on UI thread
                Issues.Clear();
                foreach (var issue in issuesList) {
                    Issues.Add(issue);
                }
            } else {
                // Need to invoke on UI thread
                dispatcher.Invoke(() => {
                    Issues.Clear();
                    foreach (var issue in issuesList) {
                        Issues.Add(issue);
                    }
                });
            }

            return issuesList.Count == 0;
        }

        public override string ToString() {
            var status = HasEphemerisData ? "Ready" : "⚠ No Ephemeris";
            return $"Track Moving Target: {CachedTargetName ?? "Unknown"} ({status})";
        }
    }
}
