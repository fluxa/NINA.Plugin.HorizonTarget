# HorizonTarget Plugin for NINA

A NINA plugin that uses NASA JPL HORIZONS system to automatically track moving targets (comets and asteroids) during imaging sessions. The plugin fetches ephemeris data and continuously adjusts telescope pointing to keep the target centered as it moves across the sky.

## Table of Contents

- [Overview](#overview)
- [Installation](#installation)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Detailed Usage Instructions](#detailed-usage-instructions)
- [Sequencer Setup Guide](#sequencer-setup-guide)
- [Target Naming Guide](#target-naming-guide-critical)
- [Important Notes](#important-notes)
- [Troubleshooting](#troubleshooting)
- [Technical Details](#technical-details)

## Overview

HorizonTarget integrates with NINA's sequencer to provide automated tracking of solar system objects that move relative to the background stars. Unlike fixed deep-sky objects, comets and asteroids require continuous position updates to remain centered in the frame during long exposure sequences.

The plugin:
- Fetches accurate ephemeris data from NASA JPL HORIZONS database
- Interpolates and extrapolates target positions for any given time
- Automatically slews the telescope when drift exceeds your threshold
- Works seamlessly with NINA's native sequence instructions

## Installation

1. Download the latest release of the HorizonTarget plugin
2. Copy the plugin DLL file to: `%LocalAppData%\NINA\Plugins\3.0.0\Fluxa.NINA.HorizonTarget\`
3. Restart NINA
4. The plugin should appear in NINA's plugin list
5. Enable the plugin if it's not already enabled

**Note:** Ensure you have NINA version 3.0.0.2017 or later installed.

## Features

- **Fetch Ephemeris from HORIZONS**: Retrieves accurate position data from NASA JPL HORIZONS system
- **Track Moving Target Instruction**: Initial slew to target position based on ephemeris data
- **Track Moving Target Trigger**: Automatically checks drift and slews before each instruction (exposures)
- **Rate-based Extrapolation**: Uses motion rates from ephemeris for accurate position prediction
- **Time-based Interpolation**: Calculates exact target position for any moment in time
- **Automatic Drift Correction**: Continuously monitors and corrects for target movement
- **Configurable Drift Threshold**: Set your own tolerance for when slews are triggered

## Prerequisites

- **NINA Version**: 3.0.0.2017 or later
- **Telescope Connection**: Telescope must be connected in NINA's Equipment tab
- **Internet Connection**: Required for fetching ephemeris data from HORIZONS
- **Valid Observatory Coordinates**: Your NINA profile must have correct latitude, longitude, and elevation set

## Quick Start

1. Create a new sequence in NINA
2. Add a **Sequential Container**
3. Add **Fetch Ephemeris from HORIZONS** instruction (first)
4. Enter the target name (e.g., "C/2025 R2")
5. Add **Track Moving Target Trigger** to the Sequential Container
6. Set drift threshold (e.g., 2 arcseconds)
7. Add your exposure instructions (e.g., Smart Exposure)
8. Run the sequence

The trigger will automatically track the target before each exposure.

## Detailed Usage Instructions

### Fetch Ephemeris from HORIZONS

This instruction fetches ephemeris data from NASA JPL HORIZONS system and caches it for use by other instructions.

**Configuration:**
- **Target Name**: The official designation of the comet or asteroid (see [Target Naming Guide](#target-naming-guide-critical) below)
- **Duration (h)**: How many hours of ephemeris data to fetch (1-48 hours)
- **Step (min)**: Time interval between ephemeris points (1-60 minutes)

**Important:**
- This instruction **must** be placed first in your sequence
- Ephemeris data is cached in memory and shared with other instructions
- The instruction always uses the current time as the start time
- **DO NOT enable "ContinueOnError"** on this instruction - if it fails, the sequence should stop

**Status Indicators:**
- Green "✓ Ephemeris Data Available" with point count when successful
- The UI will update automatically after fetching completes

### Track Moving Target

This instruction performs an initial slew to the target position. It's typically used once at the beginning of a sequence to get the telescope pointing at the target.

**Configuration:**
- No configuration options (always slews to current target position)

**Usage:**
- Place this instruction after "Fetch Ephemeris from HORIZONS"
- It will slew to the target's current position based on ephemeris data
- For precise centering, add NINA's built-in "Plate Solve" and "Center" instructions after this one

**Status Indicators:**
- Green "✓ Ready to Track" when ephemeris data is available
- Orange warning if no ephemeris data is available

### Track Moving Target Trigger

This trigger automatically checks the drift between the telescope's current position and the target's expected position before each instruction in the sequence. If the drift exceeds the configured threshold, it automatically slews to the target position.

**Configuration:**
- **Drift Threshold (arcsec)**: Maximum allowed drift before slewing (0.1-3600 arcseconds)
  - Smaller values = more frequent slews but better tracking accuracy
  - Larger values = fewer slews but target may drift more
  - Recommended: 2-5 arcseconds for most use cases

**How It Works:**
- Before each instruction (exposures, filter changes, etc.), the trigger:
  1. Gets the current UTC time
  2. Interpolates/extrapolates the target position from ephemeris data
  3. Calculates the angular distance (drift) from current telescope position
  4. If drift > threshold, slews to target position
  5. If drift ≤ threshold, continues without slewing

**Status Indicators:**
- Green "✓ Trigger Ready" when ephemeris data is available and telescope is connected
- Orange warning if no ephemeris data is available

## Sequencer Setup Guide

### Recommended Sequence Structure

```
Sequential Container
├── Triggers
│   └── Track Moving Target Trigger (Drift Threshold: 2")
├── Instructions
│   ├── Fetch Ephemeris from HORIZONS
│   │   ├── Target Name: "C/2025 R2"
│   │   ├── Duration: 24h
│   │   └── Step: 2 min
│   ├── Track Moving Target (optional - for initial positioning)
│   ├── Plate Solve (optional - for precise centering)
│   ├── Center (optional - for precise centering)
│   └── Smart Exposure
│       ├── Loop Condition: 10 iterations
│       └── Take Exposure
│           └── Exposure Time: 30s
```

### Step-by-Step Setup

1. **Create a New Sequence**
   - In NINA, go to the Sequencer tab
   - Click "New Sequence" or open an existing sequence

2. **Add Sequential Container**
   - Right-click in the sequence area
   - Select "Add Container" → "Sequential"
   - This container will hold all your instructions

3. **Add Fetch Ephemeris Instruction (FIRST)**
   - Right-click on the Sequential Container
   - Select "Add Instruction" → "Horizon Target" → "Fetch Ephemeris from HORIZONS"
   - Configure:
     - **Target Name**: Enter the official designation (see Target Naming Guide)
     - **Duration**: Set based on your session length (e.g., 24 hours for overnight)
     - **Step**: 2 minutes is usually sufficient
   - **Important**: This must be the first instruction

4. **Add Track Moving Target Trigger**
   - Right-click on the Sequential Container
   - Select "Add Trigger" → "Horizon Target" → "Track Moving Target Trigger"
   - Configure:
     - **Drift Threshold**: Set to 2-5 arcseconds (adjust based on your needs)
   - The trigger will appear in the "Triggers" section of the container

5. **Add Track Moving Target Instruction (Optional)**
   - Right-click on the Sequential Container
   - Select "Add Instruction" → "Horizon Target" → "Track Moving Target"
   - This performs an initial slew to get close to the target
   - **Optional**: Add NINA's "Plate Solve" and "Center" instructions after this for precise positioning

6. **Add Your Exposure Instructions**
   - Add your normal exposure instructions (Smart Exposure, Take Exposure, etc.)
   - The trigger will automatically track the target before each exposure

7. **Test the Sequence**
   - Connect your telescope and camera
   - Run the sequence and monitor the logs
   - Verify that the trigger is slewing when drift exceeds threshold

### Best Practices

- **Always place Fetch Ephemeris first**: It must complete successfully before other instructions can use the data
- **Set appropriate drift threshold**: 
  - For fast-moving comets: 1-2 arcseconds
  - For slower asteroids: 2-5 arcseconds
  - Too small = excessive slews, too large = target drifts out of frame
- **Use longer ephemeris duration**: If your session might run longer than expected, fetch more data (e.g., 48 hours)
- **Monitor the logs**: Check that interpolation is working correctly and slews are happening when expected
- **Initial positioning**: Use "Track Moving Target" instruction for initial slew, then let the trigger handle continuous tracking

## Target Naming Guide (CRITICAL)

**This is the most important section!** HORIZONS uses specific naming conventions that may differ from common names or designations you see elsewhere.

### Understanding HORIZONS Naming

HORIZONS requires the **official designation** as recognized by the Minor Planet Center (MPC). Common names or alternative designations often won't work.

### Comet Naming Examples

**Correct Format:**
- `C/2025 N1` - Official designation (works)
- `C/2025 R2` - Official designation (works)
- `1P/Halley` - Periodic comet designation (works)

**Incorrect Format:**
- `3I/ATLAS` - This is a common name, not the official designation
- `ATLAS` - Too generic, multiple comets share this name
- `Comet ATLAS` - Common name format, not recognized

**How to Find the Correct Name:**

1. **Use HORIZONS Web Interface:**
   - Go to https://ssd.jpl.nasa.gov/horizons/
   - Click "Web Interface"
   - In the "Target Body" field, try searching for the common name
   - HORIZONS will show you the official designation if found
   - Copy the exact designation shown

2. **Check Minor Planet Center (MPC):**
   - Visit https://minorplanetcenter.net/
   - Search for the comet or asteroid
   - Use the official designation shown

3. **Example Search Process:**
   - If you know "3I/ATLAS", search HORIZONS for "ATLAS"
   - HORIZONS may return: "C/2025 N1 (ATLAS)" or similar
   - Use the designation part: `C/2025 N1`

### Asteroid Naming Examples

**Correct Format:**
- `1` - Numbered asteroid (Ceres)
- `433` - Numbered asteroid (Eros)
- `2023 DW` - Provisional designation
- `Ceres` - Named asteroid (some work, but numbers are more reliable)

**Incorrect Format:**
- `Asteroid 1` - Don't include "Asteroid" prefix
- `(1) Ceres` - Don't include parentheses

### Common Mistakes

1. **Using common names instead of designations:**
   - ❌ "3I/ATLAS" → ✅ "C/2025 N1"
   - ❌ "Comet NEOWISE" → ✅ "C/2020 F3"

2. **Including extra text:**
   - ❌ "Comet C/2025 R2" → ✅ "C/2025 R2"
   - ❌ "Asteroid (433) Eros" → ✅ "433"

3. **Using wrong format:**
   - ❌ "2025-R2" → ✅ "C/2025 R2"
   - ❌ "2025R2" → ✅ "C/2025 R2"

### Verification

After entering a target name:
- The "Fetch Ephemeris" instruction will show a green status when data is successfully fetched
- If the name is incorrect, you'll see an error: "Target not found in HORIZONS database"
- Check the logs for detailed error messages

### Tips

- **When in doubt, use HORIZONS web interface first** to verify the exact name
- **Copy-paste the designation** directly from HORIZONS to avoid typos
- **For newly discovered objects**, allow time for MPC to assign official designations
- **Provisional designations** (e.g., "2023 DW") work but may change when objects are numbered

## Important Notes

### Error Behavior Settings

**CRITICAL**: Do NOT enable "ContinueOnError" on the "Fetch Ephemeris from HORIZONS" instruction.

- If ephemeris fetch fails, the sequence should stop
- Without ephemeris data, tracking cannot work
- The instruction will throw an error if the target name is incorrect or HORIZONS is unavailable
- Enabling "ContinueOnError" will cause the sequence to continue with invalid data

### Ephemeris Data Caching

- Ephemeris data is cached in memory after fetching
- The cache is shared between all instructions and triggers
- Changing the target name invalidates the cache
- The cache persists for the duration of the NINA session

### Mount Pointing Accuracy

- The plugin uses your mount's pointing model (TPoint) for initial slews
- Mount pointing errors will affect initial positioning
- For precise centering, add NINA's "Plate Solve" and "Center" instructions after "Track Moving Target"
- The trigger handles continuous tracking, so initial pointing accuracy is less critical for long sessions

### Drift Threshold Selection

- **Small threshold (0.5-2")**: More accurate tracking, but more frequent slews
  - Use for: Fast-moving comets, long exposures, tight framing
- **Medium threshold (2-5")**: Balanced approach
  - Use for: Most use cases, general imaging
- **Large threshold (5-10")**: Fewer slews, but target may drift
  - Use for: Slow-moving objects, wide field imaging, short exposures

### Time Considerations

- Ephemeris data is fetched for the current time forward
- The plugin interpolates positions between ephemeris points
- The plugin extrapolates positions beyond the last ephemeris point using motion rates
- For very long sessions, fetch more ephemeris data (longer duration)

### Network Requirements

- Requires internet connection to fetch ephemeris data
- HORIZONS API calls typically complete in 1-2 seconds
- If HORIZONS is unavailable, the sequence will fail (as intended)

## Troubleshooting

### "Target not found in HORIZONS database"

**Problem**: The target name you entered is not recognized by HORIZONS.

**Solutions:**
1. Verify the target name using HORIZONS web interface (https://ssd.jpl.nasa.gov/horizons/)
2. Use the official designation, not common names
3. Check for typos in the target name
4. For newly discovered objects, wait for official MPC designation
5. See [Target Naming Guide](#target-naming-guide-critical) for detailed examples

### Sequence continues after Fetch Ephemeris fails

**Problem**: The sequence continues even though ephemeris fetch failed.

**Solution:**
- Check if "ContinueOnError" is enabled on the Fetch Ephemeris instruction
- **Disable "ContinueOnError"** - this instruction must succeed for tracking to work
- The sequence should stop if ephemeris fetch fails

### Trigger not slewing

**Problem**: The trigger shows it's checking drift, but not slewing when it should.

**Solutions:**
1. Check that telescope is connected
2. Verify ephemeris data is available (green status indicator)
3. Check drift threshold - it may be set too high
4. Review logs to see calculated drift values
5. Ensure "Enable Auto Slew" is not disabled (this option was removed, so it should always be enabled)

### Target drifts out of frame

**Problem**: The target moves out of the frame during exposures.

**Solutions:**
1. Reduce drift threshold (e.g., from 5" to 2")
2. Check that trigger is actually running (check logs)
3. Verify ephemeris data covers your session time
4. For very fast-moving objects, use shorter exposure times
5. Check mount tracking is enabled

### "No ephemeris data available" warning

**Problem**: Instructions show warning that no ephemeris data is available.

**Solutions:**
1. Ensure "Fetch Ephemeris from HORIZONS" instruction is placed first
2. Verify the instruction completed successfully (green status)
3. Check that target name matches between instructions
4. If you changed target name, the cache is invalidated - re-run Fetch Ephemeris

### Interpolation/extrapolation not working

**Problem**: Positions seem incorrect or not updating.

**Solutions:**
1. Check logs for interpolation details (time, base point, rates)
2. Verify ephemeris data includes rate information (RA rate, Dec rate)
3. Check that system time is correct (plugin uses UTC)
4. Review logs to see if extrapolation warnings appear

### Sequence stuck on "Slew"

**Problem**: Sequence appears to hang during slewing operations.

**Solutions:**
1. Check telescope connection
2. Verify mount is responding
3. Check for mount driver issues
4. Review telescope logs for errors
5. Ensure target coordinates are valid (not NaN or extreme values)

## Technical Details

### How It Works

The plugin uses a three-step process to track moving targets:

1. **Ephemeris Fetching**: Retrieves position and motion rate data from NASA JPL HORIZONS
2. **Position Calculation**: Interpolates or extrapolates target position for any given time
3. **Drift Monitoring**: Continuously compares telescope position to target position and slews when needed

### Interpolation and Extrapolation

**Interpolation** (within ephemeris range):
- Finds the nearest ephemeris point before the target time
- Uses motion rates (RA rate, Dec rate) to calculate position change
- Formula: `Position = BasePosition + (Rate × TimeDelta)`

**Extrapolation** (beyond ephemeris range):
- Uses the last ephemeris point and its rates
- Extrapolates forward using constant motion assumption
- Logs a warning when extrapolating
- Accuracy decreases the further you extrapolate beyond the data range

**Rate-based Calculation**:
- Motion rates are in arcseconds per hour
- Rates are converted to degrees and applied to base position
- RA is normalized to 0-360 degrees
- Dec is clamped to -90 to +90 degrees

### Ephemeris Data Structure

Each ephemeris point contains:
- **TimeUtc**: Timestamp in UTC
- **RaDegrees**: Right Ascension in decimal degrees
- **DecDegrees**: Declination in decimal degrees
- **RaRate**: Motion rate in RA (arcseconds/hour, optional)
- **DecRate**: Motion rate in Dec (arcseconds/hour, optional)

### Cache Mechanism

- Ephemeris data is stored in static memory (`FetchEphemerisInstruction.CachedEphemeris`)
- Cache is shared across all instruction and trigger instances
- Cache is invalidated when target name changes
- Cache persists for the NINA session duration
- UI updates automatically when cache is updated via static events

### Time Handling

- All times are handled in UTC
- Current time is obtained via `DateTime.UtcNow`
- Ephemeris times from HORIZONS are in UTC
- Time deltas are calculated in hours for rate application

### Drift Calculation

- Angular distance between telescope and target is calculated using NINA's `Coordinates` class
- Distance is converted to arcseconds for comparison with threshold
- Calculation accounts for spherical geometry (great circle distance)

## Support

For issues, questions, or contributions:
- Check the logs for detailed error messages
- Review this README, especially the [Target Naming Guide](#target-naming-guide-critical) and [Troubleshooting](#troubleshooting) sections
- Verify your setup matches the [Prerequisites](#prerequisites) and [Important Notes](#important-notes)

## License

This plugin is licensed under MPL-2.0. See LICENSE.txt for details.
