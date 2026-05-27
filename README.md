# KSP EngineAnalyzer 
An advanced engine screening and planning tool built for Kerbal Space Program (KSP). It uses mathematical simulations to recommend the most suitable engines for your current spacecraft. 
 <img width="1254" height="1254" alt="BA305B7F4FF7E5A1F43EFE3C29D9E472" src="https://github.com/user-attachments/assets/5953daab-0aba-46e7-a699-918ddb80c042" />

Press <img width="58" height="56" alt="image" src="https://github.com/user-attachments/assets/fd7285f4-4f41-4652-a769-a1ed1ee60532" />
in the VAB/SPH to start. [中文说明](./docs/中文/README.md) 
 
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
- **Researched Only Filter** - When enabled, only displays engines that have been unlocked in the tech tree. 
- **Multi-Dimension Sorting** - Four sorting modes available: 
  - **Δv** - Sort by maximum delta-v (default) 
  - **TWR** - Sort by thrust-to-weight ratio 
  - **Isp** - Sort by specific impulse (automatically uses vacuum or sea level Isp based on current mode) 
  - **Value** - Sort by "Δv per Fund" to find the most cost-effective options 
- **Isp Limit** - Filters out engines above a certain Isp. Slide to 20,000 to enable "Sci-Fi Mode" (removes filter). 
- **Max TWR** - Sets the maximum allowed Thrust-to-Weight Ratio for the current mode; automatically accounts for fuel mass. 
- **Min TWR** - Sets the minimum required TWR for the current mode. 
- **Search Bar** - Quickly find engines by name. 
- **Engine Recommendation List** - `[Engine Type]` **Engine Name** {Select button spawns and configures the part}. 
  - **Second Row**: Δv (Predicted), TWR (Predicted), Isp, Total Mass (Wet), $ (Price). 
  - **Third Row**: Fuel type, Ullage requirement, Ignition count, and Rated burn time. 
- **Language Switch** - Toggle between Chinese and English interface. 
 
## Download and Installation ## 
 
This mod is generally compatible with most KSP versions (untested) and requires **RealFuels** to function. 
 
1. Download the latest release file. 
2. Install like any standard mod. The path should be:  
   `X:\...\Kerbal Space Program\GameData\EngineAnalyzer` (where X is your drive). 
3. Once installed, enter the VAB and press **Ctrl+E**. If the interface appears, the installation was successful. 

<img width="1119" height="825" alt="image" src="https://github.com/user-attachments/assets/46227730-a6df-4f13-97cd-07b60a7d7058" />

