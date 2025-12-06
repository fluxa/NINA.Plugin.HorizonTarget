using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Fluxa.NINA.HorizonTarget.Models;

namespace Fluxa.NINA.HorizonTarget.Services { 
    public class HorizonsApiService {
        private static readonly HttpClient httpClient = new HttpClient {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private const string HORIZONS_API_URL = "https://ssd.jpl.nasa.gov/api/horizons.api";

        /// <summary>
        /// Fetch ephemeris data from NASA HORIZONS API
        /// </summary>
        /// <param name="targetName">Target object (e.g., "C/2025 R2")</param>
        /// <param name="longitude">Site longitude in degrees (East positive)</param>
        /// <param name="latitude">Site latitude in degrees (North positive)</param>
        /// <param name="elevationKm">Site elevation in kilometers</param>
        /// <param name="startUtc">Start time in UTC</param>
        /// <param name="stopUtc">Stop time in UTC</param>
        /// <param name="stepMinutes">Time step in minutes</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of ephemeris points</returns>
        public async Task<List<EphemerisPoint>> FetchEphemerisAsync(
            string targetName,
            double longitude,
            double latitude,
            double elevationKm,
            DateTime startUtc,
            DateTime stopUtc,
            int stepMinutes,
            CancellationToken ct = default) {
            // Build query parameters
            var parameters = BuildQueryParameters(
                targetName,
                longitude,
                latitude,
                elevationKm,
                startUtc,
                stopUtc,
                stepMinutes
            );

            // Make HTTP request
            var queryString = string.Join("&", parameters.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var requestUrl = $"{HORIZONS_API_URL}?{queryString}";

            var response = await httpClient.GetAsync(requestUrl, ct);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync(ct);

            // Log response for debugging
            global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] HORIZONS API response received: {responseText.Length} characters");
            if (responseText.Length > 0) {
                var preview = responseText.Length > 500 ? responseText.Substring(0, 500) + "..." : responseText;
                global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] Response preview: {preview}");
            }

            // Parse the response
            return ParseHorizonsResponse(responseText);
        }

        private Dictionary<string, string> BuildQueryParameters(
            string targetName,
            double longitude,
            double latitude,
            double elevationKm,
            DateTime startUtc,
            DateTime stopUtc,
            int stepMinutes) {
            return new Dictionary<string, string> {
                ["format"] = "json",
                ["COMMAND"] = $"'{targetName}'",
                ["OBJ_DATA"] = "NO",
                ["MAKE_EPHEM"] = "YES",
                ["EPHEM_TYPE"] = "OBSERVER",
                ["CENTER"] = "'coord@399'",  // Topocentric site on Earth
                ["SITE_COORD"] = $"'{longitude},{latitude},{elevationKm}'",
                ["START_TIME"] = $"'{startUtc:yyyy-MM-dd HH:mm}'",
                ["STOP_TIME"] = $"'{stopUtc:yyyy-MM-dd HH:mm}'",
                ["STEP_SIZE"] = $"'{stepMinutes} m'",
                ["QUANTITIES"] = "'1,3'",  // 1=Astrometric RA/Dec, 3=Rates
                ["CAL_FORMAT"] = "CAL",
                ["CAL_TYPE"] = "M",
                ["ANG_FORMAT"] = "DEG",  // Decimal degrees for easier parsing
                ["APPARENT"] = "AIRLESS",
                ["TIME_DIGITS"] = "MINUTES",
                ["EXTRA_PREC"] = "YES",
                ["CSV_FORMAT"] = "YES"  // Critical for JSON mode
            };
        }

        private List<EphemerisPoint> ParseHorizonsResponse(string responseText) {
            var ephemeris = new List<EphemerisPoint>();

            if (string.IsNullOrWhiteSpace(responseText)) {
                throw new Exception("HORIZONS API returned empty response");
            }

            // Check for common error messages first
            var upperResponse = responseText.ToUpperInvariant();
            if (upperResponse.Contains("NO MATCHES FOUND") || 
                upperResponse.Contains("NO MATCHING") ||
                (upperResponse.Contains("Matching small-bodies:") && upperResponse.Contains("No matches"))) {
                throw new Exception("Target not found in HORIZONS database. Please verify the target name is correct (e.g., 'C/2025 R2' for comets or asteroid designation for asteroids).");
            }

            // HORIZONS JSON response has the ephemeris data as CSV within the result field
            // When format=json, the response is a JSON object with a "result" field
            string textToParse = responseText;

            // Check if response is JSON format
            if (responseText.TrimStart().StartsWith("{") || responseText.TrimStart().StartsWith("[")) {
                global::NINA.Core.Utility.Logger.Debug("[HorizonTarget] Detected JSON format response, extracting result field");
                try {
                    var jsonObj = JObject.Parse(responseText);
                    if (jsonObj.TryGetValue("result", out var resultToken)) {
                        textToParse = resultToken.Value<string>();
                        global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] Extracted result field: {textToParse?.Length ?? 0} characters");
                    } else {
                        global::NINA.Core.Utility.Logger.Warning("[HorizonTarget] JSON response missing 'result' field, attempting to parse as text");
                        // Fall through to text parsing
                    }
                } catch (Newtonsoft.Json.JsonException ex) {
                    global::NINA.Core.Utility.Logger.Warning($"[HorizonTarget] Failed to parse JSON response, attempting text parsing: {ex.Message}");
                    // Fall through to text parsing
                }
            } else {
                global::NINA.Core.Utility.Logger.Debug("[HorizonTarget] Detected text format response");
            }

            if (string.IsNullOrWhiteSpace(textToParse)) {
                throw new Exception("HORIZONS response contains no parseable data");
            }

            // Find the data section between $$SOE and $$EOE markers
            var soeIndex = textToParse.IndexOf("$$SOE");
            var eoeIndex = textToParse.IndexOf("$$EOE");

            if (soeIndex == -1 || eoeIndex == -1) {
                global::NINA.Core.Utility.Logger.Error($"[HorizonTarget] Missing $$SOE or $$EOE markers. SOE index: {soeIndex}, EOE index: {eoeIndex}");
                global::NINA.Core.Utility.Logger.Error($"[HorizonTarget] Text to parse preview: {(textToParse.Length > 1000 ? textToParse.Substring(0, 1000) + "..." : textToParse)}");
                throw new Exception("Invalid HORIZONS response: missing $$SOE or $$EOE markers. The response may be in an unexpected format or contain an error message.");
            }

            var dataSection = textToParse.Substring(soeIndex + 5, eoeIndex - soeIndex - 5).Trim();
            var lines = dataSection.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] Found {lines.Length} lines in ephemeris data section");

            int parsedCount = 0;
            int errorCount = 0;

            foreach (var line in lines) {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // CSV format from HORIZONS: Date, (empty), 'm', RA(deg), Dec(deg), dRA, dDec, ...
                // Example: "2025-Nov-28 02:42, ,m,   0.058825239,   7.617632167,  86.82564,  24.95160,"
                var parts = line.Split(',').Select(p => p.Trim()).ToArray();

                // Need at least 5 columns: Date, empty, 'm', RA, Dec
                if (parts.Length < 5) {
                    global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] Skipping line with insufficient columns ({parts.Length}): {line}");
                    continue;
                }

                try {
                    // Parse date (format: YYYY-MMM-DD HH:MM) - column 0
                    var dateStr = parts[0].Trim();
                    if (string.IsNullOrWhiteSpace(dateStr)) {
                        global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] Skipping line with empty date: {line}");
                        continue;
                    }
                    var timeUtc = ParseHorizonsDate(dateStr);

                    // Skip column 1 (empty) and column 2 ('m' unit indicator)
                    // Parse RA and Dec (already in decimal degrees) - columns 3 and 4
                    if (!double.TryParse(parts[3], out var ra)) {
                        throw new FormatException($"Failed to parse RA from column 3: '{parts[3]}'");
                    }
                    if (!double.TryParse(parts[4], out var dec)) {
                        throw new FormatException($"Failed to parse Dec from column 4: '{parts[4]}'");
                    }

                    // Optional: parse rates if available - columns 5 and 6
                    double? raRate = null;
                    double? decRate = null;

                    if (parts.Length >= 7) {
                        if (double.TryParse(parts[5], out var raRateVal))
                            raRate = raRateVal;
                        if (double.TryParse(parts[6], out var decRateVal))
                            decRate = decRateVal;
                    }

                    ephemeris.Add(new EphemerisPoint {
                        TimeUtc = timeUtc,
                        RaDegrees = ra,
                        DecDegrees = dec,
                        RaRate = raRate,
                        DecRate = decRate
                    });
                    parsedCount++;
                } catch (Exception ex) {
                    errorCount++;
                    global::NINA.Core.Utility.Logger.Warning($"[HorizonTarget] Failed to parse ephemeris line: {line}. Error: {ex.Message}");
                }
            }

            global::NINA.Core.Utility.Logger.Debug($"[HorizonTarget] Parsed {parsedCount} ephemeris points, {errorCount} errors");

            if (ephemeris.Count == 0) {
                throw new Exception($"No ephemeris data parsed from HORIZONS response. Found {lines.Length} lines but none were valid. Check logs for parsing errors.");
            }

            global::NINA.Core.Utility.Logger.Info($"[HorizonTarget] Successfully parsed {ephemeris.Count} ephemeris points");
            return ephemeris;
        }

        private DateTime ParseHorizonsDate(string dateStr) {
            // HORIZONS format: "2025-Nov-27 16:00"
            // We need to handle the month abbreviation

            try {
                return DateTime.Parse(dateStr, System.Globalization.CultureInfo.InvariantCulture);
            } catch {
                // Fallback: try manual parsing if DateTime.Parse fails
                // Format: YYYY-MMM-DD HH:MM
                var parts = dateStr.Split(new[] { ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5) {
                    var year = int.Parse(parts[0]);
                    var month = ParseMonthAbbreviation(parts[1]);
                    var day = int.Parse(parts[2]);
                    var hour = int.Parse(parts[3]);
                    var minute = int.Parse(parts[4]);

                    return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
                }

                throw new FormatException($"Unable to parse HORIZONS date: {dateStr}");
            }
        }

        private int ParseMonthAbbreviation(string monthAbbr) {
            var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
                ["Jan"] = 1,
                ["Feb"] = 2,
                ["Mar"] = 3,
                ["Apr"] = 4,
                ["May"] = 5,
                ["Jun"] = 6,
                ["Jul"] = 7,
                ["Aug"] = 8,
                ["Sep"] = 9,
                ["Oct"] = 10,
                ["Nov"] = 11,
                ["Dec"] = 12
            };

            if (months.TryGetValue(monthAbbr, out var month))
                return month;

            throw new FormatException($"Unknown month abbreviation: {monthAbbr}");
        }
    }
}