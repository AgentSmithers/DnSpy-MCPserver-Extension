# dnSpyEx MCPServer Extension
This is a dnSpy MCPServer Extension for the use of automated AI .NET reverse engineering
The Example1.Extension has been updated to include the MCPServer logic with a few starting function to feed the decompiled code back to the LLM.
This is an ongoing project and I plan to continue to add commands as time persist.

To execute the server, run as Administrator so the the Http server may Listen on +:3001.
The following command will suffice:
cd {dnspy directory}
dnspy.exe --extension-directory {Path to extension}
Ex. dnspy.exe --extension-directory "C:\Users\User\source\repos\dnSpy\Extensions\Examples\Example1.Extension\bin\Debug\net48"

In alternative you may copy the release into the Extension folder within the dnSpy\Extensions\AgnetSmithersMCPServer path.
