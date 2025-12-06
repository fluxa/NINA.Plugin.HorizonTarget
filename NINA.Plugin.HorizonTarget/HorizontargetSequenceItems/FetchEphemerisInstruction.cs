using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using Fluxa.NINA.HorizonTarget.Models;
using Fluxa.NINA.HorizonTarget.Services;
using NINA.Profile.Interfaces;
using NINA.Astrometry;
using System.Windows.Threading;

namespace Fluxa.NINA.HorizonTarget.HorizontargetSequenceItems {
    [ExportMetadata("Name", "Fetch Ephemeris from HORIZONS")]
    [ExportMetadata("Description", "Fetches moving target ephemeris data from NASA JPL HORIZONS system. IMPORTANT: Do NOT enable 'ContinueOnError' on this instruction - ephemeris data is required for tracking.")]
    [ExportMetadata("Icon", "HorizonTargetSVG")]
    [ExportMetadata("Category", "Horizon Target")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class FetchEphemerisInstruction : SequenceItem, IValidatable {
        
        private readonly IProfileService profileService;
        private readonly HorizonsApiService horizonsService;

        // Static event to notify when ephemeris cache is updated
        public static event EventHandler EphemerisCacheUpdated;

        [ImportingConstructor]
        public FetchEphemerisInstruction(IProfileService profileService) {
            this.profileService = profileService;
            this.horizonsService = new HorizonsApiService();

            // Set defaults
            TargetName = "C/2025 R2";
            DurationHours = 24;
            StepMinutes = 2;
        }

        // Make a copy constructor for cloning
        private FetchEphemerisInstruction(FetchEphemerisInstruction copyMe) : this(copyMe.profileService) {
            CopyMetaData(copyMe);
        }

        public override object Clone() {
            return new FetchEphemerisInstruction(this) {
                TargetName = TargetName,
                DurationHours = DurationHours,
                StepMinutes = StepMinutes
            };
        }

        #region Properties

        private string targetName;
        [JsonProperty]
        public string TargetName {
            get => targetName;
            set {
                targetName = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasEphemerisData)); // Invalidate cached data if target changes
                RaisePropertyChanged(nameof(CachedEphemerisCount));
            }
        }

        private double durationHours;
        [JsonProperty]
        public double DurationHours {
            get => durationHours;
            set {
                durationHours = Math.Max(1, Math.Min(48, value)); // Limit 1-48 hours
                RaisePropertyChanged();
            }
        }

        private int stepMinutes;
        [JsonProperty]
        public int StepMinutes {
            get => stepMinutes;
            set {
                stepMinutes = Math.Max(1, Math.Min(60, value)); // Limit 1-60 minutes
                RaisePropertyChanged();
            }
        }

        // This stores the fetched ephemeris data (not saved to JSON, in-memory only)
        // Static cache allows sharing between FetchEphemerisInstruction and TrackMovingTargetInstruction
        public static List<EphemerisPoint> CachedEphemeris { get; private set; }
        public static string CachedTargetName { get; private set; }

        /// <summary>
        /// Indicates whether ephemeris data is available (for UI binding)
        /// </summary>
        public bool HasEphemerisData {
            get {
                return CachedEphemeris != null && CachedEphemeris.Count > 0 && CachedTargetName == TargetName;
            }
        }

        /// <summary>
        /// Gets the number of cached ephemeris points (for display)
        /// </summary>
        public int CachedEphemerisCount {
            get => CachedEphemeris?.Count ?? 0;
        }

        #endregion

        private void NotifyEphemerisCacheUpdated() {
            // Raise PropertyChanged for this instance
            RaisePropertyChanged(nameof(HasEphemerisData));
            RaisePropertyChanged(nameof(CachedEphemerisCount));

            // Notify all other instances via static event
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher.CheckAccess()) {
                EphemerisCacheUpdated?.Invoke(this, EventArgs.Empty);
            } else {
                dispatcher.BeginInvoke(new Action(() => {
                    EphemerisCacheUpdated?.Invoke(this, EventArgs.Empty);
                }));
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                // Get observatory coordinates from NINA profile
                var longitude = profileService.ActiveProfile.AstrometrySettings.Longitude;
                var latitude = profileService.ActiveProfile.AstrometrySettings.Latitude;
                var elevationMeters = profileService.ActiveProfile.AstrometrySettings.Elevation;
                var elevationKm = elevationMeters / 1000.0;

                // Determine time range - always use current time
                var startUtc = DateTime.UtcNow;
                // Round down to nearest minute
                startUtc = new DateTime(startUtc.Year, startUtc.Month, startUtc.Day,
                    startUtc.Hour, startUtc.Minute, 0, DateTimeKind.Utc);

                var stopUtc = startUtc.AddHours(DurationHours);

                // Update progress
                progress?.Report(new ApplicationStatus {
                    Status = $"Fetching ephemeris for {TargetName}..."
                });

                // Call HORIZONS API
                var ephemeris = await horizonsService.FetchEphemerisAsync(
                    TargetName,
                    longitude,
                    latitude,
                    elevationKm,
                    startUtc,
                    stopUtc,
                    StepMinutes,
                    token
                );

                // Cache the results
                CachedEphemeris = ephemeris;
                CachedTargetName = TargetName;

                // Notify all instances that cache was updated
                NotifyEphemerisCacheUpdated();

                // Success!
                progress?.Report(new ApplicationStatus {
                    Status = $"Ephemeris fetched: {ephemeris.Count} points from {ephemeris[0].TimeUtc:yyyy-MM-dd HH:mm} to {ephemeris[^1].TimeUtc:yyyy-MM-dd HH:mm} UTC"
                });

                global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] Fetched {ephemeris.Count} ephemeris points for {TargetName}");
            } catch (OperationCanceledException) {
                // Re-throw cancellation exceptions as-is
                throw;
            } catch (Exception ex) {
                progress?.Report(new ApplicationStatus {
                    Status = $"Failed to fetch ephemeris: {ex.Message}",
                    Status2 = ex.ToString()
                });

                global::NINA.Core.Utility.Logger.Error($"[HorizonTarget] Ephemeris fetch failed: {ex}");
                
                // IMPORTANT: This is a CRITICAL failure - the sequence cannot proceed without ephemeris data.
                // If "ContinueOnError" is enabled on this instruction, the sequence will continue but will fail
                // on subsequent instructions that require ephemeris data. 
                // RECOMMENDATION: Do NOT enable "ContinueOnError" on the Fetch Ephemeris instruction.
                var errorMessage = $"HORIZONS API error: {ex.Message}. " +
                    $"CRITICAL: Ephemeris data is required for tracking. " +
                    $"If 'ContinueOnError' is enabled, disable it to stop the sequence on failure.";
                
                global::NINA.Core.Utility.Logger.Error($"[HorizonTarget] {errorMessage}");
                
                throw new SequenceEntityFailedException(errorMessage, ex);
            }
        }

        public IList<string> Issues { get; set; } = new List<string>();

        public bool Validate() {
            Issues.Clear();

            if (string.IsNullOrWhiteSpace(TargetName)) {
                Issues.Add("Target name is required");
            }

            if (DurationHours < 1 || DurationHours > 48) {
                Issues.Add("Duration must be between 1 and 48 hours");
            }

            if (StepMinutes < 1 || StepMinutes > 60) {
                Issues.Add("Step size must be between 1 and 60 minutes");
            }

            return Issues.Count == 0;
        }

        public override string ToString() {
            return $"Fetch Ephemeris: {TargetName} ({DurationHours}h @ {StepMinutes}m steps)";
        }
    }
}
