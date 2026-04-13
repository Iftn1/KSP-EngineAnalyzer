# KSP EngineAnalyzer
An advanced engine screening and planning tool built for Kerbal Space Program (KSP). It uses mathematical simulations to recommend the most suitable engines for your current spacecraft.

Press **Ctrl+E** in the VAB/SPH to start. [中文说明](./docs/中文/README.md)

### Features ###
- **Vacuum Mode** - Optimizes for vacuum engines using vacuum Isp and thrust values. Toggle to switch to "Sea Level Mode".
- **Sea Level Mode** - Calculations based on sea level Isp and thrust. Filters out engines with extremely low sea level Isp or massive thrust drop-offs.
- **Normal Analysis** - Recommends engines providing the maximum Δv based on your Isp limits and TWR range. Toggle to switch to "Reverse Planning".
- **Reverse Planning** - Recommends engines based on a target Δv and minimum TWR. You can use the slider or manual input for TWR (Experimental).
- **Lock Current Stage** - Records current mass and tank volume to prepare for the next stage calculation.
- **Reset All** - Reinitializes the program (use this when starting a new vessel or clearing errors).
- **Sync VAB Data** - Updates data after placing tanks or modifying parts.
- **Tank Volume (kL)** - Automatically calculates volume by subtracting previously locked stages from the total volume. Supports manual input.
- **Cluster** - Simulates multi-engine configurations (supports up to 12 engines).
- **Rocket, Jet, SRB Filters** - Toggle lights to show or filter specific engine types.
- **Sort by Value** - Sorts engines by "Δv per Fund" to find the most cost-effective options.
- **Isp Limit** - Filters out engines above a certain Isp. Slide to 20,000 to enable "Sci-Fi Mode" (removes filter).
- **Max TWR** - Sets the maximum allowed Thrust-to-Weight Ratio for the current mode; automatically accounts for fuel mass.
- **Min TWR** - Sets the minimum required TWR for the current mode.
- **Search Bar** - Quickly find engines by name.
- **Engine Recommendation List** - `[Engine Type]` **Engine Name** {Select button spawns and configures the part}.
   - **Second Row**: Δv (Predicted), TWR (Predicted), Isp, Total Mass (Wet), $ (Price).
   - **Third Row**: Fuel type, Ullage requirement, Ignition count, and Rated burn time.

## Download and Installation ##

This mod is generally compatible with most KSP versions (untested) and requires **RealFuels** to function.

1. Download the latest release file.
2. Install like any standard mod. The path should be:  
   `X:\...\Kerbal Space Program\GameData\EngineAnalyzer` (where X is your drive).
3. Once installed, enter the VAB and press **Ctrl+E**. If the interface appears, the installation was successful.
<img width="857" height="713" alt="f254d3f3651913bf14a835f2bb7ae364" src="https://github.com/user-attachments/assets/cf49e697-d182-47ee-b508-47194dadbade" />
