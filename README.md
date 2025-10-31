# AgentSmithers dnSpyEx MCPServer Extension
This is a dnSpy MCPServer Extension for the use of automated AI .NET reverse engineering
The Example1.Extension has been updated to include the MCPServer logic with a few starting function to feed the decompiled code back to the LLM.
This is an ongoing project and I plan to continue to add commands as time persist.

To execute the server, run as Administrator so the the Http server may Listen on +:3001 (release is currently set to 3003).

The following command will suffice:

cd {dnspy directory}

dnspy.exe --extension-directory {Path to extension}

Ex. dnspy.exe --extension-directory "C:\repos\dnSpy\Extensions\Examples\Example1.Extension\bin\Debug\net48"

Example 2 in CMD: C:\Users\user>"C:\Users\user\source\repos\DnSpy-MCPserver-Extension\dnSpy\dnSpy\bin\Debug\net48\dnspy.exe" --extension-directory "C:\Users\user\source\repos\DnSpy-MCPserver-Extension\dnSpy\dnSpy\bin\Debug\net48\Extensions"

In alternative you may copy the release into the Extension folder within the dnSpy\Extensions\AgentSmithersMCPServer path.

# Modifying the project
Ensure to compile the primary DnSpy Soultion (Clean and rebuild) so that the dependancies for "AgentSmithers DnSpyEx MCPServer" can use them for its own build. (Note: When compiling DnSpyEx you may have approx. ~20 errors, this will not prevent the 45 Projects from compiling)
<img width="846" alt="image" src="https://github.com/user-attachments/assets/4d9269fa-ab0e-4392-8042-f79b31795e43" />
<img width="991" alt="image" src="https://github.com/user-attachments/assets/57e708b3-05e9-4edd-8b72-53c0850c5304" />

Once dnSpy is fully cleaned and compiled, navigate to the Example1 Extension Project. In there is the modified code.
Make your adjustments, compile then use the --extension-directory argument when executing DnSpy.exe to point to your projects extension path.

In alternative you may pull down the latest dnSpyEx then rename the Extension sample folder to another name and bring the modified extension folder over to recompile.

# Supported commands
Currently readonly commands are supported to enumerate active namespaces, classes and functions by the AI. Plans are in the works to allow the AI to make interactive adjustments to the decompiled assemblies.
```
Get_Selected_Node - Gets the currently selected node within dnSpyEx
Get_Loaded_Assemblies - Gets all Assemblys currently loaded within dnSpyEx
Namespaces_From_Assembly - Dumps all unique namespaces under a given Assembly
Classes_From_Namespace - List all Classes under a given Namespace
Get_Class_Sourcecode - Dumps a target Class sourcecode
Get_Method_Prototypes - List all Method prototypes from a given Class within a given Namespace
Get_Method_SourceCode - Dumps a target Method's sourcecode
Update_Method_SourceCode - Update a target Method's sourcecode
Get_Function_Opcodes - Returns the IL opcodes of the specified method (with source line numbers)
Set_Function_Opcodes - Modifies the IL of the specified method at a given IL line index
Update_Tabs_View - Update all active tabs to reflect any changes or adjustments
Rename_Namespace - Renames exactly one distinct namespace across all types
Rename_Class - Renames a specific class within a given Namespace
Rename_Method - Renames a specific Methods by Class within a given Namespace
These commands allow you to inspect and modify .NET assemblies loaded in dnSpyEx, including viewing and editing source code, IL code, and performing various renaming operations.
```
# Once the extension is loaded
A messagebox will appear allowing you a chance to attach your debugger if nessessary
![image](https://github.com/user-attachments/assets/f7a53b4c-e273-435e-9098-d92eb54fa84e)

Click "OK" and the MCPServer will load on :3001

Connect via SSE or if your application only supports STDIO (Claude Desktop / Windsurf) grab a copy of the STDIO<->SSE bridge
https://github.com/AgentSmithers/MCPProxy-STDIO-to-SSE

Attach the Bridge AFTER hitting "OK" and your good to go.

![image](https://github.com/user-attachments/assets/b8fa494e-2962-4733-a1f9-83f729c29811)

![image](https://github.com/user-attachments/assets/851e1ef0-e9ee-400b-b185-4cbde3739894)
