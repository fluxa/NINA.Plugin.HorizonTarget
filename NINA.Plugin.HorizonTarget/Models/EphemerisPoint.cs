using System;

namespace Fluxa.NINA.HorizonTarget.Models {
    public class EphemerisPoint {
        /// <summary>
        /// UTC timestamp for this ephemeris point
        /// </summary>
        public DateTime TimeUtc { get; set; }

        /// <summary>
        /// Right Ascension in decimal degrees
        /// </summary>
        public double RaDegrees { get; set; }

        /// <summary>
        /// Declination in decimal degrees
        /// </summary>
        public double DecDegrees { get; set; }

        /// <summary>
        /// Optional: Rate of motion in RA (arcsec/hour)
        /// </summary>
        public double? RaRate { get; set; }

        /// <summary>
        /// Optional: Rate of motion in Dec (arcsec/hour)
        /// </summary>
        public double? DecRate { get; set; }

        public override string ToString() {
            return $"{TimeUtc:yyyy-MM-dd HH:mm} UTC - RA: {RaDegrees:F6}° Dec: {DecDegrees:F6}°";
        }
    }
}