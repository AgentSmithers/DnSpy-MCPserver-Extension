# AgentSmithers dnSpyEx MCPServer Extension
This is a dnSpy MCPServer Extension for the use of automated AI .NET reverse engineering
The Example1.Extension has been updated to include the MCPServer logic with a few starting function to feed the decompiled code back to the LLM.
This is an ongoing project and I plan to continue to add commands as time persist.

To execute the server, run as Administrator so the the Http server may Listen on +:3001.
The following command will suffice:
cd {dnspy directory}
dnspy.exe --extension-directory {Path to extension}
Ex. dnspy.exe --extension-directory "C:\repos\dnSpy\Extensions\Examples\Example1.Extension\bin\Debug\net48"

In alternative you may copy the release into the Extension folder within the dnSpy\Extensions\AgnetSmithersMCPServer path.

# Modifying the project
The easiest way to modify this project is to pull the full respository and navigate to the Example1 Extension. In there is the modified code.
Make your adjustments, compile then use the --extension-directory to point to your projects extension path.

In alternative you may pull down the latest dnSpyEx then rename the Extension sample folder to another name and bring the modified extension folder over to recompile.

# Supported commands
Currently readonly commands are supported to enumerate active namespaces, classes and functions by the AI. Plans are in the works to allow the AI to make interactive adjustments to the decompiled assemblies.

# Once the extension is loaded
A messagebox will appear allowing you a chance to attach your debugger if nessessary
![image](https://github.com/user-attachments/assets/f7a53b4c-e273-435e-9098-d92eb54fa84e)

Click "OK" and the MCPServer will load on :3001

Connect via SSE or if your application only supports STDIO (Claude Desktop / Windsurf) grab a copy of the STDIO<->SSE bridge
https://github.com/AgentSmithers/MCPProxy-STDIO-to-SSE

Attach the Bridge AFTER hitting "OK" and your good to go.

![image](https://github.com/user-attachments/assets/b8fa494e-2962-4733-a1f9-83f729c29811)

![image](https://github.com/user-attachments/assets/851e1ef0-e9ee-400b-b185-4cbde3739894)
