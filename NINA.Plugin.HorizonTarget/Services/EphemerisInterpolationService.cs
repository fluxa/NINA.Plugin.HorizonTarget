using System;
using System.Collections.Generic;
using System.Linq;
using Fluxa.NINA.HorizonTarget.Models;
using NINA.Astrometry;

namespace Fluxa.NINA.HorizonTarget.Services {
    /// <summary>
    /// Service for interpolating comet positions from ephemeris data using rate-based extrapolation
    /// </summary>
    public class EphemerisInterpolationService {
        /// <summary>
        /// Interpolates the comet position for a given UTC time using rate-based extrapolation
        /// </summary>
        /// <param name="targetTimeUtc">Target time in UTC</param>
        /// <param name="ephemeris">List of ephemeris points (must be sorted by TimeUtc)</param>
        /// <returns>Interpolated coordinates</returns>
        public Coordinates InterpolatePosition(DateTime targetTimeUtc, List<EphemerisPoint> ephemeris) {
            if (ephemeris == null || ephemeris.Count == 0) {
                throw new ArgumentException("Ephemeris data is empty or null", nameof(ephemeris));
            }

            // Find the nearest ephemeris point (before or at target time)
            EphemerisPoint basePoint = null;
            bool isExtrapolating = false;

            // Find the point at or just before the target time
            for (int i = ephemeris.Count - 1; i >= 0; i--) {
                if (ephemeris[i].TimeUtc <= targetTimeUtc) {
                    basePoint = ephemeris[i];
                    break;
                }
            }

            // If no point found before target time, use the first point (extrapolating backward)
            if (basePoint == null) {
                basePoint = ephemeris[0];
                isExtrapolating = true;
                global::NINA.Core.Utility.Logger.Warning($"[HorizonTarget] Target time {targetTimeUtc:yyyy-MM-dd HH:mm:ss} UTC is before ephemeris range (first: {ephemeris[0].TimeUtc:yyyy-MM-dd HH:mm:ss}). Using first point and extrapolating backward.");
            }

            // Check if we're extrapolating forward beyond the last point
            var lastPoint = ephemeris[ephemeris.Count - 1];
            if (targetTimeUtc > lastPoint.TimeUtc) {
                isExtrapolating = true;
                basePoint = lastPoint;
                global::NINA.Core.Utility.Logger.Warning($"[HorizonTarget] Target time {targetTimeUtc:yyyy-MM-dd HH:mm:ss} UTC is after ephemeris range (last: {lastPoint.TimeUtc:yyyy-MM-dd HH:mm:ss}). Using last point and extrapolating forward.");
            }

            // Calculate time delta in hours
            var timeDelta = (targetTimeUtc - basePoint.TimeUtc).TotalHours;
            
            // Log interpolation/extrapolation details
            var mode = isExtrapolating ? "EXTRAPOLATING" : "INTERPOLATING";
            global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] {mode}: Target time: {targetTimeUtc:yyyy-MM-dd HH:mm:ss} UTC, Base point: {basePoint.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC, Time delta: {timeDelta:F4} hours ({timeDelta * 60:F2} minutes)");
            global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] Base point position: RA: {basePoint.RaDegrees:F6}° ({basePoint.RaDegrees / 15.0:F6}h), Dec: {basePoint.DecDegrees:F6}°");

            // Start with base position
            double raDegrees = basePoint.RaDegrees;
            double decDegrees = basePoint.DecDegrees;

            // Apply rate-based extrapolation if rates are available
            if (basePoint.RaRate.HasValue && basePoint.DecRate.HasValue) {
                // Rates are in arcseconds per hour
                // Convert to degrees: arcsec/hour * hours / 3600 arcsec/degree
                var raChangeDegrees = (basePoint.RaRate.Value * timeDelta) / 3600.0;
                var decChangeDegrees = (basePoint.DecRate.Value * timeDelta) / 3600.0;

                global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] Applying rates: RA rate: {basePoint.RaRate.Value:F4}\"/hour, Dec rate: {basePoint.DecRate.Value:F4}\"/hour");
                global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] Position change: RA: {raChangeDegrees * 3600:F4}\" ({raChangeDegrees:F6}°), Dec: {decChangeDegrees * 3600:F4}\" ({decChangeDegrees:F6}°)");

                raDegrees += raChangeDegrees;
                decDegrees += decChangeDegrees;

                // Normalize RA to 0-360 degrees
                while (raDegrees < 0) raDegrees += 360.0;
                while (raDegrees >= 360.0) raDegrees -= 360.0;

                // Clamp Dec to -90 to +90 degrees
                decDegrees = Math.Max(-90.0, Math.Min(90.0, decDegrees));
            } else if (isExtrapolating) {
                global::NINA.Core.Utility.Logger.Warning($"[HorizonTarget] No rate data available for extrapolation. Using base point position without adjustment.");
            } else {
                global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] No rate data available. Using base point position (interpolation without rates).");
            }

            // Convert RA from degrees to hours for NINA Coordinates
            // NINA Coordinates expects RA in hours (0-24)
            double raHours = raDegrees / 15.0;

            // Create Angle objects using static factory methods
            // Angle.ByHours() for RA and Angle.ByDegree() for Dec
            var raAngle = Angle.ByHours(raHours);
            var decAngle = Angle.ByDegree(decDegrees);

            // Create and return NINA Coordinates object
            var result = new Coordinates(raAngle, decAngle, Epoch.J2000);
            
            // Log final calculated position
            global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] Calculated position: RA: {result.RAString} ({raDegrees:F6}°), Dec: {result.DecString} ({decDegrees:F6}°)");
            
            return result;
        }

        /// <summary>
        /// Gets the angular distance between two coordinates in arcseconds
        /// </summary>
        public double GetAngularDistanceArcsec(Coordinates coord1, Coordinates coord2) {
            if (coord1 == null || coord2 == null) {
                return double.MaxValue;
            }

            // NINA Coordinates supports subtraction operator that returns an Angle
            var distance = (coord1 - coord2)?.Distance;
            if (distance == null) {
                return double.MaxValue;
            }

            // Convert from degrees to arcseconds
            return distance.Degree * 3600.0;
        }
    }
}

