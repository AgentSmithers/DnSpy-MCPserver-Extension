using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.TreeView;
using ICSharpCode.TreeView;
using static Example1.Extension.SimpleMcpServer;

namespace Example1.Extension
{
    class MCPCommands
    {
		[Command("Help", MCPCmdDescription = "Call this command before executing any other dnSpyEx command for assistance on how to pass arguments.")]
		public static string Help() {
			return @"You are working with a MCPServer that provides Methods for inspecting and manipulating .NET assemblies inside dnSpy. 
				Each Method returns a string containing the requested information. Your job is to invoke these Methods using correct argument names and types, 
				capture their return values in string variables, and use the information as needed.

				dnSpy holds information in a hierarchy with multiple assemblys loaded.
				Assembly
				└── Namespace
					├── ClassA
					├── ClassB
					└── ClassC
						├── Method1()
						├── Method2()
						└── Method3()

				Explanation of the layout:
				Assembly is the root.
				Under the assembly, Namespace groups all related types.
				Within the namespace, each Class is listed with a ├── (if not last) or └── (if last).
				Methods inside a class are further indented under that class, also using ├──/└── to show their order.

				Here are a listt of commands, their format and what they return:
				Get_Loaded_Assemblies()
					Takes no parameters and returns a newline-delimited list of all loaded assembly names.
				Example of what the Get_Loaded_Assemblies returns:
				dnlib
				System.Runtime
				System.Private.CoreLib
				System.Web.Services
				mscorlib
				System
				System.Xml
				Microsoft.VisualBasic
				System.Web
				System.Data
				System.Web.Extensions
				AjaxControlToolkit

				Namespaces_From_Assembly(string assemblyName)
					Takes one parameter named assemblyName, which should match the exact assembly name returned from Get_Loaded_Assemblies.
					Example Method call: Namespaces_From_Assembly(""System.Runtime"");
				Example of what the Namespaces_From_Assembly returns:
				System
				System.Runtime.Serialization
				System.Runtime.Serialization.Configuration
				System.Runtime.Serialization.Diagnostics
				System.Runtime.Serialization.Diagnostics.Application
				System.Runtime.Serialization.Json
				System.Text
				System.Xml

				Get_Global_Namespaces()
					Takes no parameters.
					Returns a newline-delimited list of all types in the global namespace (i.e., no explicit namespace).

				Classes_From_Namespace(string assemblyName, string namespaceName)
					Takes two parameters—the assembly name returned from Get_Loaded_Assemblies and a namespace returned from Namespaces_From_Assembly.
					Example Method call: Classes_From_Namespace(""System.Runtime"", ""System.Runtime.Serialization.Json"");

				GetCurrentlySelectItem()
					Takes no parameters.
					Returns a descriptive string for the currently selected node in the dnSpy tree view. Useful for context-aware operations.
					Possible return formats: ""Assembly Document: [Name]"", ""Namespace: [Name]"", ""Class Type: [Name]"", ""MethodNode: [Name]"", etc., or an empty string if nothing is selected, or an error message.
				Example Method call: GetCurrentlySelectItem()
				Example Return: ""Class Type: String"" or ""MethodNode: Substring""

				Get_Method_Prototypes(string Assembly, string Namespace, string ClassName)
					Takes three parameters—the name of the assembly, the namespace, and the class name—and returns a newline-delimited list of all Method prototypes in that class.
					Example Method call: Get_Method_Prototypes(""MyAssemblyName"", ""MyNamespace"", ""MyClassName"");

				Get_Class_Sourcecode(string assemblyName, string namespaceName, string className)
					Takes three parameters—the assembly name, the namespace, and the class name—and returns the full source code of that class.

				Get_Method_SourceCode(string Assembly, string Namespace, string ClassName, string MethodName)
					Takes four parameters—the assembly name, the namespace, the class name, and the Method name—and returns the full source code of the specified Method.
					Example Method call: Get_Method_SourceCode(""MyAssemblyName"", ""MyNamespace"", ""MyClassName"", ""MyMethodName"");

				Update_Method_SourceCode(string Assembly, string Namespace, string ClassName, string MethodName, string Source)
					Takes five parameters: the assembly, namespace, class, and method names to target, plus a 'Source' string containing the C# code for the new method body.
					Attempts to replace the target method's body with the provided source code. This involves internal decompilation and recompilation.
					Returns a confirmation message indicating success or failure, or an error message.
				Example Method call: Update_Method_SourceCode(""MyLib"", ""MyNamespace"", ""MyClass"", ""MyMethod"", ""Console.WriteLine(\""Hello!\""; return 1;"")

				Get_Function_Opcodes(string assemblyName, string @namespace, string className, string methodName)
					Takes four parameters: the assembly, namespace, class, and method names.
					Returns the IL (Intermediate Language) opcodes of the specified method, formatted with line numbers, offsets, opcode names, and operands.
				Example Method call: Get_Function_Opcodes(""System.Runtime"", ""System"", ""String"", ""IsNullOrEmpty"")
				Example Return (excerpt):
				// IL for System.Runtime:System.String.IsNullOrEmpty
				// #    Offset   OpCode     Operand
				// ----------------------------------------------------------
				// 1    0000     ldarg.0
				// 2    0001     brfalse.s  0008
				...

				Set_Function_Opcodes(string assemblyName, string @namespace, string className, string methodName, string[] ilOpcodes, int ilLineNumber, string mode)
					Takes seven parameters: target identifiers (assembly, namespace, class, method), an array of strings 'ilOpcodes' representing IL instructions, a 0-based 'ilLineNumber' index, and a 'mode' string (""Overwrite"" or ""Append"").
					Modifies the IL of the target method. ""Overwrite"" replaces instructions starting at 'ilLineNumber'. ""Append"" inserts the new instructions at 'ilLineNumber', shifting existing ones down.
					The 'ilOpcodes' array should contain strings like ""Ldstr \""Hello\"""", ""Call System.Console::WriteLine(System.String)"", ""Ret"". Basic operand parsing is supported.
					Returns a confirmation message or an error/exception message.
				Example Method call: Set_Function_Opcodes(""MyLib"", ""MyNs"", ""MyClass"", ""MyMethod"", new string[] { ""nop"", ""nop"" }, 5, ""Append"")
				Example Return: ""✅ Appended 2 instructions at IL line 5.""

				Overwrite_Full_Function_Opcodes(string assemblyName, string @namespace, string className, string methodName, string[] ilOpcodes)
					Takes five parameters: target identifiers (assembly, namespace, class, method) and an array of strings 'ilOpcodes' representing the new IL instructions.
					Completely replaces the entire IL body of the target method with the provided opcodes. All existing instructions are removed first.
					The 'ilOpcodes' format is the same as for Set_Function_Opcodes. Basic operand parsing is supported.
					Returns a confirmation message or an error/exception message.
				Example Method call: Overwrite_Full_Function_Opcodes(""MyLib"", ""MyNs"", ""MyClass"", ""MyMethod"", new string[] { ""ldstr \""Overwritten!\"""", ""call System.Console::WriteLine(System.String)"", ""ret"" })
				Example Return: ""✅ Overwrote IL of MyClass.MyMethod""

				RefreshAllOpenTabs()
					Takes no parameters.
					Refreshes all currently open document tabs (e.g., source code views) in dnSpy to reflect any modifications made to the underlying assemblies.
					Returns a confirmation message or an exception message.
				Example Method call: RefreshAllOpenTabs()
				Example Return: ""Document tabs refreshed""

				Rename_Namespace(string Assembly, string oldNamespace, string newNamespace)
					Takes three parameters—the assembly name,the existing namespace and the new namespace—and renames exactly one distinct namespace across all types. Returns a summary of how many types were updated.
					Example Method call: Rename_Namespace(""MyAssemblyName"", ""Old.Namespace"", ""New.Namespace"");

				Rename_Class(string Assembly, string Namespace, string oldClassName, string newClassName)
					Takes four parameters—the assembly name,the namespace containing the class, the current class name, and the new class name—and renames a specific class within that namespace.
					Example Method call: Rename_Class(""MyAssemblyName"", ""MyNamespace"", ""OldClassName"", ""NewClassName"");

				Rename_Method(string Assembly, string Namespace, string ClassName, string MethodName, string newName)
					Takes five parameters—the assembly name,the namespace, the class name, the current Method name (or substring match), and the new name—and renames a specific Method in the given class. Returns a confirmation message.
					Example Method call: Rename_Method(""MyAssemblyName"", ""MyNamespace"", ""MyClassName"", ""OldMethodName"", ""NewMethodName"");
			";
		}

		[Command("Get_Selected_Node", MCPCmdDescription = "Gets the currently selected node within dnSpyEx")]
		public static string GetCurrentlySelectItem() {
			try {
				// 1) we declare a local to capture the result
				object selected = null;

				// 2) marshal the read to the TreeView's UI thread
				var tv = Global.MyTreeView.TreeView;
				
				Global.MyAppWindow.MainWindow.Dispatcher.Invoke(() => {
					selected = tv.SelectedItem;
				});

				// 3) nothing selected?
				if (selected == null)
					return string.Empty;

				// 4) if it’s a DocumentTreeNodeData (or one of its sub-interfaces), pull out a name
				if (selected is DocumentTreeNodeData docNode) {
					// e.g. for methods/types you might want the metadata name: //"dnSpy.Documents.TreeView.MethodNodeImpl"}
					if (docNode is dnSpy.Contracts.Documents.TreeView.AssemblyDocumentNode tn)
						return "Assembly Document: " + tn.NodePathName.Name;
					if (docNode is dnSpy.Contracts.Documents.TreeView.AssemblyReferenceNode an)
						return "Assembly Reference: " + an.NodePathName.Name;
					
					if (docNode is dnSpy.Contracts.Documents.TreeView.NamespaceNode nn)
						return "Namespace: " + nn.NodePathName.Name;
					if (docNode is dnSpy.Contracts.Documents.TreeView.TypeNode tyn)
						return "Class Type: " + tyn.NodePathName.Name;

					if (docNode is dnSpy.Contracts.Documents.TreeView.MethodNode mn)
						return "MethodNode: " + mn.NodePathName.Name;
					if (docNode is dnSpy.Contracts.Documents.TreeView.FieldNode fn)
						return "FieldNode: " + fn.NodePathName.Name;
					// …etc…
					// fallback:
					var type = docNode.GetType();
					return docNode.Icon.Name + ": " + docNode.NodePathName.Name;
				}

				// 5) otherwise just ToString()
				return selected.ToString()!;
			}
			catch (Exception ex) {
				return $"Exception: {ex.Message}";
			}
		}

		[Command("Get_Global_Namespaces", MCPCmdDescription = "List all types in the global namespace (i.e., no explicit namespace).")]
		public static string Get_Global_Namespaces() {
			try {
				var sb = new StringBuilder();
				Debug.WriteLine("- Global Namespace Types -");

				// Gather all TypeDefs from all modules
				var globalTypes = Global.MyTreeView
					.GetAllModuleNodes()
					.SelectMany(mod => mod.TreeNode.Data.GetModule().GetTypes())
					.Where(t => string.IsNullOrEmpty(t.Namespace))
					.OrderBy(t => t.FullName);

				// Output each type
				foreach (var type in globalTypes) {
					Debug.WriteLine($"	{type.FullName}");
					sb.AppendLine(type.FullName);
				}

				if (sb.Length == 0) {
					return "No types found in the global namespace.";
				}
				return sb.ToString();
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}

		[Command("Get_Loaded_Assemblies", MCPCmdDescription = "Gets all Assemblys currently loaded within dnSpyEx")]
		public static string DumpLoadedAssemblies() {
			try { 
				string DataToReturn = "";
				Debug.WriteLine("-ModuleDocumentNode-");
				ModuleDef MyModule;
				foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
					MyModule = Modnode.GetModule();
					Debug.WriteLine("\t" + MyModule.Name + "->" + MyModule.Assembly.Name + " Location: " + MyModule.Location);
					DataToReturn += MyModule.Assembly.Name + "\r\n";
				}
				return DataToReturn;
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}

		[Command("Namespaces_From_Assembly", MCPCmdDescription = "Dumps all unique namespaces under a given Assembly.")]
		public static string DumpNamespacesFromAssembly(string AssemblyName) {
			try { 
				var sb = new StringBuilder();
				Debug.WriteLine("- Unique Namespaces -");

				// 1) Gather all TypeDefs from modules whose assembly name matches the filter
				var myTypes = Global.MyTreeView
					.GetAllModuleNodes()
					.Where(mod => mod.GetModule().Assembly.Name.StartsWith(AssemblyName, StringComparison.OrdinalIgnoreCase))
					.SelectMany(mod => mod.TreeNode.Data.GetModule().GetTypes())
					.ToList();

				// 2) Extract distinct namespace strings
				var uniqueNamespaces = myTypes
					.Select(t => t.Namespace)
					.Where(ns => !string.IsNullOrEmpty(ns))
					.Distinct()
					.OrderBy(ns => ns);

				// 3) Output each namespace
				foreach (var ns in uniqueNamespaces) {
					Debug.WriteLine("\t" + ns);
					sb.AppendLine(ns);
				}

				return sb.ToString();
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}

		[Command("Classes_From_Namespace", MCPCmdDescription = "List all Classes under a given Namespace.")]
		public static string DumpClassesFromNamespace(string AssemblyName, string Namespace) {
			try { 
				string DataToReturn = "";
				Debug.WriteLine("-ModuleDocumentNodes-");
				ModuleDef Assemblies;
				foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
					Assemblies = Modnode.GetModule();
					Debug.WriteLine(Assemblies.Name);				
					if (Assemblies.Assembly.Name==(AssemblyName)) { //Assemblies.Name = *.dll
						DataToReturn += Assemblies.Assembly.Name + "\r\n";
						var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().OrderBy(t => t.Name.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
						foreach (TypeDef MyType in ModTypes) {
							if (MyType.Namespace == Namespace) {
								if (MyType.FullName.StartsWith(Namespace)) {
									Debug.WriteLine(MyType.FullName); //The Class
									DataToReturn += "\t" + MyType.FullName + "\r\n";
								}
							}
						}
					}
				}
				return DataToReturn;
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}


		[Command("Get_Class_Sourcecode", MCPCmdDescription = "Dumps a target Class sourcecode")]
		public static string DumpClassCode(string Assembly, string Namespace, string ClassName) { //Dumps a Classes sourcecode
			try {
				string DataToReturn = "";
				//Debug.WriteLine("-MethodDef-");

				ModuleDef MyModuleDef;
				foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
					MyModuleDef = Modnode.GetModule();
					if (MyModuleDef.Assembly.Name == (Assembly)) {
						Debug.WriteLine("\t" + MyModuleDef.Name); //GemBox.Spreadsheet.dll
																  //Debug.WriteLine("\t" + Modnode.GetModule().Name);
																  //DataToReturn += Modnode.GetModule().Name + "\r\n";

						var ModNode = Modnode.TreeNode.Data.GetModuleNode();
						var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToList();
						//DataToReturn += "\t" + DumpNode(Modnode.TreeNode, 1);
						foreach (TypeDef MyType in ModTypes) {
							Debug.WriteLine("\t" + MyType.FullName);
							if (MyType.Namespace == Namespace) {
								if (MyType.Name == ClassName) {
									if (string.IsNullOrEmpty(Namespace) ? MyType.FullName == ClassName : MyType.FullName.StartsWith(Namespace + "." + ClassName)) { //+MethodName
																																									//Debug.WriteLine(TheExtension.DumpSource(Modnode, MyType)); //The class as a whole
										DataToReturn += TheExtension.DumpSource(Modnode, MyType);
									}
								}
							}
						}
					}
				}
				return DataToReturn;
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}

		/*
		[Command("Get_Class_Sourcecode", MCPCmdDescription = "Dumps a target Class sourcecode")]
		public static string DumpClassCode(string Assembly, string Namespace, string ClassName) { //Dumps a Classes sourcecode
			try {
				string DataToReturn = "";
				//Debug.WriteLine("-MethodDef-");

				ModuleDef MyModuleDef;
				foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
					MyModuleDef = Modnode.GetModule();
					if (MyModuleDef.Assembly.Name==(Assembly)) {
						Debug.WriteLine("\t" + MyModuleDef.Name); //GemBox.Spreadsheet.dll
						//Debug.WriteLine("\t" + Modnode.GetModule().Name);
						//DataToReturn += Modnode.GetModule().Name + "\r\n";

						var ModNode = Modnode.TreeNode.Data.GetModuleNode();
						var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToList();
						//DataToReturn += "\t" + DumpNode(Modnode.TreeNode, 1);
						foreach (TypeDef MyType in ModTypes) {
							Debug.WriteLine("\t" + MyType.FullName);
							if (MyType.Namespace == Namespace) {
								if (MyType.Name == ClassName) {
									if (MyType.FullName.StartsWith(Namespace + "." + ClassName)) { //+MethodName
																								   //Debug.WriteLine(TheExtension.DumpSource(Modnode, MyType)); //The class as a whole
										DataToReturn += TheExtension.DumpSource(Modnode, MyType);
									}
								}
							}
						}
					}
				}
				return DataToReturn;
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}
		*/

		[Command("Get_Method_Prototypes", MCPCmdDescription = "List all Method prototypes from a givin Class within a given Namespace.")]
		public static string DumpMethodPrototypes(string Assembly, string Namespace, string ClassName) {
			try { 
				string DataToReturn = "";
				//Debug.WriteLine("-MethodDef-");

				ModuleDef MyModuleDef;
				foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
					MyModuleDef = Modnode.GetModule();
					if (MyModuleDef.Assembly.Name==(Assembly)) {
						Debug.WriteLine("\t" + MyModuleDef.Name);
						DataToReturn += MyModuleDef.Name + "\r\n";
						var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToList();
						foreach (TypeDef MyType in ModTypes) 
						{
							Debug.WriteLine("\t" + MyType.FullName);		
							//if (MyType.FullName.StartsWith(Namespace + "." + ClassName)) { //+MethodName
							if (MyType.Namespace == Namespace) 
							{ 
								if (MyType.Name == ClassName) 
								{
									DataToReturn += "\t" + MyType.FullName + "\r\n";
									List<MethodDef> Methods = MyType.Methods.OrderBy(t => t.Name.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
									foreach (MethodDef MyMethod in Methods) 
									{
										DataToReturn += "\t\t" + MyMethod.FullName + "\r\n";
									}
								}
							}
						}
					}
				}
				return DataToReturn;
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}

		[Command("Get_Method_SourceCode", MCPCmdDescription = "Dumps a target Method's sourcecode")]
		public static string DumpMethodsSourcode(string Assembly, string Namespace, string ClassName, string MethodName) {
			try {
				string DataToReturn = "";
				//Debug.WriteLine("-MethodDef-");
				ModuleDef MyModuleDef;
				foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
					MyModuleDef = Modnode.GetModule();
					if (MyModuleDef.Assembly.Name == (Assembly)) {
						Debug.WriteLine("\t" + MyModuleDef.Name);
						//DataToReturn += MyModuleDef.Name + "\r\n";
						var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToList();
						foreach (TypeDef MyType in ModTypes) {
							Debug.WriteLine("\t" + MyType.FullName);
							if (MyType.Namespace == Namespace) {
								if (MyType.Name == ClassName) {
									//DataToReturn += "\t" + MyType.FullName + "\r\n";
									List<MethodDef> Methods = MyType.Methods.OrderBy(t => t.Name.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
									foreach (MethodDef MyMethod in Methods) {
										if (MyMethod.Name == (MethodName)) {
											DataToReturn += TheExtension.DumpSource(Modnode, MyMethod);
										}
									}
								}
							}
						}
					}
				}
				return DataToReturn;
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}

		[Command("Update_Method_SourceCode", MCPCmdDescription = "Update a target Method's sourcecode using C#, Source argument Example: Console.WriteLine(\"Hello from patched method!\"); return \"TestedValue\";")]
		public static string Update_Methods_Sourcode(string Assembly, string Namespace, string ClassName, string MethodName, string Source) {
			try {
				string DataToReturn = "";
				//Debug.WriteLine("-MethodDef-");
				ModuleDef MyModuleDef;
				foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
					MyModuleDef = Modnode.GetModule();
					if (MyModuleDef.Assembly.Name == (Assembly)) {
						Debug.WriteLine("\t" + MyModuleDef.Name);
						//DataToReturn += MyModuleDef.Name + "\r\n";
						var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToList();
						foreach (TypeDef MyType in ModTypes) {
							Debug.WriteLine("\t" + MyType.FullName);
							if (MyType.Namespace == Namespace) {
								if (MyType.Name == ClassName) {
									//DataToReturn += "\t" + MyType.FullName + "\r\n";
									List<MethodDef> Methods = MyType.Methods.OrderBy(t => t.Name.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
									foreach (MethodDef MyMethod in Methods) {
										if (MyMethod.Name == (MethodName)) {
											DataToReturn += TheExtension.UpdateSource(Modnode, MyMethod, Source);
										}
									}
								}
							}
						}
					}
				}
				return DataToReturn;
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}

		[Command("Get_Function_Opcodes", MCPCmdDescription = "Returns the IL opcodes of the specified method (with source line numbers)")]
		public static string Get_Function_Opcodes(string assemblyName, string @namespace, string className, string methodName) 
		{
			try {
				// scan every loaded module
				foreach (ModuleDocumentNode modNode in Global.MyTreeView.GetAllModuleNodes()) {
					var module = modNode.GetModule();
					if (!string.Equals(module.Assembly.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
						continue;

					// find the type
					var type = module.GetTypes()
									 .FirstOrDefault(t =>
										 t.Namespace == @namespace &&
										 t.Name == className
									 );
					if (type == null)
						continue;

					// find the method
					var method = type.Methods
									 .FirstOrDefault(m =>
										 string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
									 );
					if (method == null)
						continue;

					// build the output
					var sb = new StringBuilder();
					sb.AppendLine($"// IL for {assemblyName}:{@namespace}.{className}.{methodName}");
					sb.AppendLine($"// #    Offset    OpCode     Operand");
					sb.AppendLine(new string('-', 70));

					int lineNo = 0;
					foreach (var instr in method.Body.Instructions) {
						lineNo++;
						// IL offset
						var offset = instr.Offset.ToString("X4");
						// mnemonic
						var opName = instr.OpCode.Name;
						// operand if present
						var operand = instr.Operand?.ToString() ?? "";

						sb.AppendLine(
							$"{lineNo,3}   {offset,-8} {opName,-10} {operand}"
						);
					}

					return sb.ToString();
				}

				return $"⚠️ Method {className}.{methodName} not found in assembly {assemblyName}";
			}
			catch (Exception ex) {
				return $"❌ Exception: {ex.Message}";
			}
		}

		[Command("Set_Function_Opcodes", MCPCmdDescription = @"Modifies the IL of the specified method at a given IL line index. mode = ""Overwrite"" → replaces existing instructions starting at that index mode = ""Append"" → inserts new instructions at that index, shifting old ones (IL lines are 0-based, so 0 means the very first instruction.). Example of IlOpcodes: ""Ldstr Hello, world!"",""Call System.Console::WriteLine(System.String)"",""Ret""")]
		public static string Set_Function_Opcodes(string assemblyName, string @namespace, string className, string methodName, string[] ilOpcodes, int ilLineNumber, string mode) {
			string DataToReturn = Global.MyAppWindow.MainWindow.Dispatcher.Invoke(() => Set_Function_Opcodes_Func(assemblyName, @namespace, className, methodName, ilOpcodes, ilLineNumber, mode)) as string;
			return DataToReturn;
		}

		public static string Set_Function_Opcodes_Func(string assemblyName, string @namespace, string className, string methodName, string[] ilOpcodes, int ilLineNumber, string mode) {
			try {
				foreach (ModuleDocumentNode modNode in Global.MyTreeView.GetAllModuleNodes()) {
					var module = modNode.GetModule();
					if (!string.Equals(module.Assembly.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
						continue;

					var type = module.GetTypes()
									 .FirstOrDefault(t => t.Namespace == @namespace && t.Name == className);
					if (type == null) continue;

					var method = type.Methods
									 .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));
					if (method == null) continue;

					var instrs = method.Body.Instructions;
					int existingCount = instrs.Count;

					// allow 0 through existingCount (inclusive at start, exclusive at end)
					if (ilLineNumber < 0 || ilLineNumber > existingCount) {
						return $"⚠️ Cannot target IL line {ilLineNumber}: method has only {existingCount} instruction{(existingCount == 1 ? "" : "s")}, so valid range is 0–{existingCount}.";
					}

					// Build new IL instructions (unchanged)
					var injected = new List<Instruction>();
					var lineRe = new Regex(@"^\s*(\S+)(?:\s+(.+))?$");
					var callRe = new Regex(@"^(?:\S+\s+)?(?<type>[\w\.]+)::(?<method>\w+)\((?<params>.*)\)$");

					foreach (var raw in ilOpcodes) {
						var line = raw.Trim();
						if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
							continue;

						var m = lineRe.Match(line);
						if (!m.Success) continue;

						var opName = m.Groups[1].Value.Trim();

						var normalized = opName.Replace('.', '_');

						var fld = typeof(OpCodes).GetField(normalized,
										 BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
						if (fld == null)
							return $"⚠️ Unknown OpCode '{opName}'";
						var code = (OpCode)fld.GetValue(null)!;
						var operandText = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";

						if (opName.Equals("Ldstr", StringComparison.OrdinalIgnoreCase)) {
							injected.Add(Instruction.Create(code, operandText));
						}
						else if (opName.Equals("Call", StringComparison.OrdinalIgnoreCase)) {
							var cm = callRe.Match(operandText);
							if (!cm.Success)
								return $"⚠️ Cannot parse Call operand '{operandText}'";

							var typeName = cm.Groups["type"].Value;
							var shortName = cm.Groups["method"].Value;
							var paramsSection = cm.Groups["params"].Value;
							var paramNames = string.IsNullOrWhiteSpace(paramsSection)
								 ? Array.Empty<string>()
								 : paramsSection.Split(',').Select(p => p.Trim()).ToArray();

							var targetType = Type.GetType(typeName, throwOnError: true);
							var candidates = targetType.GetMethods(
								BindingFlags.Public | BindingFlags.NonPublic |
								BindingFlags.Static | BindingFlags.Instance)
								.Where(mi => mi.Name == shortName).ToArray();
							if (candidates.Length == 0)
								return $"⚠️ No method '{shortName}' on '{typeName}'";

							var chosen = candidates.FirstOrDefault(mi => mi.GetParameters().Length == paramNames.Length)
										 ?? candidates[0];
							var iMethod = module.Import(chosen);
							injected.Add(Instruction.Create(code, iMethod));
						}
						else {
							injected.Add(Instruction.Create(code));
						}
					}

					// Splice into IL at exactly ilLineNumber
					int idx = ilLineNumber;
					if (mode.Equals("Overwrite", StringComparison.OrdinalIgnoreCase)) {
						for (int i = 0; i < injected.Count && idx < instrs.Count; i++)
							instrs.RemoveAt(idx);
					}
					for (int i = injected.Count - 1; i >= 0; i--)
						instrs.Insert(idx, injected[i]);

					Global.MyTreeView.TreeView.RefreshAllNodes();
					return $"✅ {(mode.Equals("Overwrite", StringComparison.OrdinalIgnoreCase) ? "Overwrote" : "Appended")} {injected.Count} instruction{(injected.Count == 1 ? "" : "s")} at IL line {ilLineNumber}.";
				}

				return $"⚠️ Method {className}.{methodName} not found in assembly {assemblyName}";
			}
			catch (Exception ex) {
				return $"❌ Exception: {ex.Message}";
			}
		}


		[Command("Overwrite_Full_Func_Opcodes", MCPCmdDescription = "Overwrites a whole method’s ILcode with the provided opcode lines, all other code for this function is removed. ilOpcodes argument is an array of strings Ex. \"Ldstr Hello, world!\",\r\n\"Call System.Console:WriteLine\",\r\n\"Ret\"")]
		public static string Overwrite_Full_Function_Opcodes(string assemblyName, string @namespace, string className, string methodName, string[] ilOpcodes) 
		{
			try {
				foreach (ModuleDocumentNode modNode in Global.MyTreeView.GetAllModuleNodes()) {
					var module = modNode.GetModule();
					if (!string.Equals(module.Assembly.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
						continue;

					var type = module.GetTypes()
									 .FirstOrDefault(t => t.Namespace == @namespace && t.Name == className);
					if (type == null)
						continue;

					var method = type.Methods
									 .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));
					if (method == null)
						continue;

					// Clear existing IL
					var instrs = method.Body.Instructions;
					instrs.Clear();

					var lineRe = new Regex(@"^\s*(\S+)(?:\s+(.+))?$");
					foreach (var raw in ilOpcodes) {
						var line = raw.Trim();
						if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
							continue;

						var m = lineRe.Match(line);
						if (!m.Success)
							continue;

						var opName = m.Groups[1].Value;
						var operandText = m.Groups[2].Success ? m.Groups[2].Value : "";

						// reflect the OpCode
						var fld = typeof(OpCodes).GetField(opName,
							BindingFlags.Public | BindingFlags.Static);
						if (fld == null)
							return $"⚠️ Unknown OpCode '{opName}'";
						var code = (OpCode)fld.GetValue(null)!;

						// now create the instruction
						if (opName == "Ldstr") {
							instrs.Add(Instruction.Create(code, operandText));
						}
						else if (opName == "Call") {
							// operandText: "Namespace.Type:MethodName,arg1,arg2"
							// split off method lookup from parameter values
							var parts = operandText.Split(new[] { ':' }, 2);
							var typeName = parts[0];
							var rest = parts.Length > 1 ? parts[1] : "";
							var methodParts = rest.Split(',').Select(s => s.Trim()).ToArray();
							var methodShortName = methodParts[0];
							var paramValues = methodParts.Skip(1).ToArray();

							var targetType = Type.GetType(typeName, throwOnError: true);
							// find all same-name methods:
							var candidates = targetType.GetMethods(
								BindingFlags.Public | BindingFlags.NonPublic |
								BindingFlags.Static | BindingFlags.Instance
							).Where(mi => mi.Name == methodShortName).ToArray();

							MethodInfo chosen;
							// pick overload by matching parameter count
							var byCount = candidates.FirstOrDefault(mi =>
								mi.GetParameters().Length == paramValues.Length);
							if (byCount != null)
								chosen = byCount;
							else
								chosen = candidates.First(); // fallback

							// parse each operand to its CLR type:
							var parsedOperands = new object[paramValues.Length];
							var paramInfos = chosen.GetParameters();
							for (int i = 0; i < paramValues.Length; i++) {
								var piType = paramInfos[i].ParameterType;
								// only string support for now:
								if (piType == typeof(string))
									parsedOperands[i] = paramValues[i];
								else
									parsedOperands[i] = Convert.ChangeType(paramValues[i], piType);
							}

							// import the MethodInfo into the module
							var iMethod = module.Import(chosen);
							// and build the call instruction
							instrs.Add(Instruction.Create(code, iMethod));
							// if non-string params you’d follow with Ldarg or Ldc_ as needed,
							// but most user-supplied Call patches will be simple Console.WriteLine(string).
						}
						else {
							instrs.Add(Instruction.Create(code));
						}
					}

					// refresh the UI
					Global.MyTreeView.TreeView.RefreshAllNodes();
					return $"✅ Overwrote IL of {className}.{methodName}";
				}

				return $"⚠️ Method {className}.{methodName} not found in {assemblyName}";
			}
			catch (Exception ex) {
				return $"❌ Exception: {ex.Message}";
			}
		}

		[Command("Update_Tabs_View", MCPCmdDescription = "Update all active tabs to reflect any changes or adjustments.")]
		public static string RefreshAllOpenTabs() {
			string DataToReturn = Global.MyAppWindow.MainWindow.Dispatcher.Invoke(() => RefreshAllOpenTabs_func()) as string;
			return DataToReturn;
		}
		public static string RefreshAllOpenTabs_func() {
			try {
				// 1) Grab a snapshot of all currently open tabs
				var openTabs = Global.MyDocumentTabService.SortedTabs.ToList();
				Global.MyDocumentTabService.Refresh(openTabs);
				// 2) For each tab: remember its document, close it, then re-open it
				//foreach (var tab in openTabs) {
				//	IDocumentViewer doc = tab.TryGetDocumentViewer();
				//	// close the existing tab
				//	documentTabService.Refresh(doc.DocumentTab);
				//	documentTabService.Close(tab);
				//	// re-open it (activate:false so we don't steal focus)
				//	documentTabService.Refresh(doc, activate: false);
				//}
				return "Document tabs refreshed";
			}
			catch (Exception ex) {
				return "Exception " + ex.Message;
			}
		}

		[Command("Rename_Namespace", MCPCmdDescription = "Renames exactly one distinct namespace across all types.")]
		public static string RenameNamespace(string Assembly, string Old_Namespace_Name, string New_Namespace_Name) {
			try {
				// 1) Gather every TypeDef from every module
				var allTypes = Global.MyTreeView
					.GetAllModuleNodes()
					.SelectMany(modNode => modNode.TreeNode.Data.GetModule().GetTypes());

				// 2) Pull out only the distinct namespace strings
				var distinctNamespaces = allTypes
					.Select(t => t.Namespace)
					.Distinct()
					.ToList();

				// 3) Find matches exactly equal to OldNamespace
				var matches = distinctNamespaces
					.Where(ns => ns == Old_Namespace_Name)
					.ToList();

				if (matches.Count == 1) {
					// 4) Rename every TypeDef that was in that namespace
					int renamedCount = 0;
					foreach (var mytype in allTypes.Where(t => t.Namespace == Old_Namespace_Name)) { //This works for renaming the class, not correct for namespace.
						mytype.Namespace = New_Namespace_Name;
						UTF8String Mystring = New_Namespace_Name;
						mytype.Name = Mystring;
						renamedCount++;
					}
					var type = matches[0];
					
					return $"Namespace '{Old_Namespace_Name}' renamed to '{New_Namespace_Name}' in {renamedCount} types.";
				}
				else if (matches.Count == 0) {
					return $"No namespace found matching '{Old_Namespace_Name}'.";
				}
				else {
					// (shouldn't really happen, since we're matching ==, but just in case)
					return $"Multiple namespaces found matching '{Old_Namespace_Name}': {string.Join(", ", matches)}. Rename aborted.";
				}
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
			
		}

		[Command("Rename_Class", MCPCmdDescription = "Renames a specific class within a given Namespace.")]
		public static string RenameClass(string Assembly, string Namespace, string OldClassName, string NewClassName) { //Not validated yet
			try {
				if (!Debugger.IsAttached) {
					return "Method not available at this time until the developer validates this function";
				}
				var matches = new List<TypeDef>();
				foreach (ModuleDocumentNode ModNode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
					ModuleDef MyModule = ModNode.GetModule();
					if (MyModule.Assembly.Name == (Assembly)) {
						var types = ModNode.TreeNode.Data.GetModule().GetTypes().ToList();

						foreach (TypeDef myType in types) {
							Debug.WriteLine(myType.FullName + " " + myType.Name);
						}
						//matches.AddRange(types.Where(t => t.FullName.StartsWith(Namespace + "." + OldClassName)));
						matches.AddRange(types.Where(t => t.Name == (OldClassName)));
					}
				}

				if (matches.Count == 1) {
					var type = matches[0];
					type.Name = NewClassName;
					return $"{Namespace}.{OldClassName} renamed to {NewClassName} successfully";
				}
				else if (matches.Count == 0) {
					return $"No classes found matching '{OldClassName}' in namespace {Namespace}.";
				}
				else {
					var found = string.Join(", ", matches.Select(t => t.FullName));
					return $"Multiple classes found matching '{OldClassName}': {found}. Be more specific, Rename aborted.";
				}
			}
			catch(Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}

		[Command("Rename_Method", MCPCmdDescription = "Renames a specific Methods by Class within a given Namespace.")]
		public static string RenameMethod(string Assembly, string Namespace, string ClassName, string MethodName, string Newname) {
			try 
			{ 
				// collect all candidate Methods
				var matches = new List<MethodDef>();

				foreach (ModuleDocumentNode ModNode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
					ModuleDef MyModule = ModNode.GetModule();
					if (MyModule.Assembly.Name==(Assembly)) {
						var ModTypes = ModNode.TreeNode.Data.GetModule().GetTypes().ToList();
						foreach (TypeDef MyType in ModTypes) {
							if (MyType.Namespace == Namespace) {
								if (MyType.Name == (ClassName)) {
									// add any Method whose name contains the target MethodName
									matches.AddRange(
										MyType.Methods
											  .Where(m => m.Name.Contains(MethodName))
									);
								}
							}
						}
					}
				}

				// decide based on how many matches we got
				if (matches.Count == 1) {
					matches[0].Name = Newname;
					return $"{Namespace}->{ClassName}->{MethodName} renamed to {Newname} successfully";
				}
				else if (matches.Count == 0) {
					return $"No Methods found matching '{MethodName}' in {Namespace}.{ClassName}.";
				}
				else {
					// list the ambiguous matches
					var found = string.Join(", ", matches.Select(m => m.Name));
					return $"Multiple Methods found matching '{MethodName}': {found}. Be more specific, Rename aborted.";
				}
			}
			catch (Exception ex) {
				return $"Exception: " + ex.Message;
			}
		}
























		//These Methods are not in use



		public static string PatchMethodLogEntry(string assemblyName, string @namespace, string className, string methodName) {
			try {
				foreach (ModuleDocumentNode modNode in Global.MyTreeView.GetAllModuleNodes()) {
					var module = modNode.GetModule();
					if (!string.Equals(module.Assembly.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
						continue;

					// find your type
					var type = module.GetTypes()
									 .FirstOrDefault(t => t.Namespace == @namespace && t.Name == className);
					if (type == null)
						continue;

					// find your method
					var method = type.Methods
									 .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));
					if (method == null)
						continue;

					// 1) import Console.WriteLine(string) into this module
					var writeLineRef = module.Import(
						typeof(Console).GetMethod(nameof(Console.WriteLine), new[] { typeof(string) })
					);

					// 2) inject at the top of the method
					IList<Instruction> instructions = method.Body.Instructions;
					instructions.Insert(0, Instruction.Create(OpCodes.Ldstr, $"[dnSpyPatch] Entering {methodName}"));
					instructions.Insert(1, Instruction.Create(OpCodes.Call, writeLineRef));

					Global.MyTreeView.TreeView.RefreshAllNodes();

					return $"✅ Successfully patched {methodName} in {className}";
				}

				return $"⚠️ Could not find method {className}.{methodName} in assembly {assemblyName}";
			}
			catch (Exception ex) {
				return $"❌ Exception: {ex.Message}";
			}
		}

		//[Command("Dump.Method.From.Class", MCPCmdDescription = "Dumps all Methods by Class within a given Namespace. Set 'DumpCode' = true to view the code within the Class as a whole.")]
		public static string DumpClasses(string Assembly, string Namespace, string ClassName, bool DumpMethods = false, bool DumpCode = false) { //Dumps a Class and its Methods
			string DataToReturn = "";
			//Debug.WriteLine("-MethodDef-");

			ModuleDef MyModuleDef;
			foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
				MyModuleDef = Modnode.GetModule();
				if (MyModuleDef.Name.Contains(Assembly)) {
					Debug.WriteLine("\t" + MyModuleDef.Name); //GemBox.Spreadsheet.dll
					if (MyModuleDef.Name.ToString().Contains(Namespace)) {
						//Debug.WriteLine("\t" + Modnode.GetModule().Name);
						if (!DumpCode) {
							DataToReturn += Modnode.GetModule().Name + "\r\n";
						}

						var ModNode = Modnode.TreeNode.Data.GetModuleNode();
						var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToList();
						//DataToReturn += "\t" + DumpNode(Modnode.TreeNode, 1);
						foreach (TypeDef MyType in ModTypes) {
							Debug.WriteLine("\t" + MyType.FullName);
							if (MyType.FullName.StartsWith(Namespace + "." + ClassName)) { //+MethodName
								if (!DumpCode) {
									if (ModNode != null) {
										//Debug.WriteLine("\t\t" + MyType.FullName); //The Class
										DataToReturn += "\t" + MyType.FullName + "\r\n";
										//Debug.WriteLine(DumpSource(modNode, MyType)); //The class as a whole
									}
									if (DumpMethods) {
										List<MethodDef> Methods = MyType.Methods.OrderBy(t => t.Name.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
										foreach (MethodDef MyMethod in Methods) {
											//Debug.WriteLine("\t\t\t" + MyMethod.FullName); //The specific Method, Way too much data
											DataToReturn += "\t\t" + MyMethod.FullName + "\r\n";
										}
									}

								}
								if (DumpCode) {
									//Debug.WriteLine(TheExtension.DumpSource(Modnode, MyType)); //The class as a whole
									DataToReturn += TheExtension.DumpSource(Modnode, MyType);
								}
							}
						}
					}
				}
			}
			return DataToReturn;
		}

		//[Command("Dump.Specific.Method", MCPCmdDescription = "Dumps all Methods by Class within a given Namespace. Set 'DumpCode' = true to view the code within the Class as a whole.")]
		public static string DumpMethods(string Assembly, string Namespace, string ClassName, string MethodName, bool DumpCode = false) {
			string DataToReturn = "";
			//Debug.WriteLine("-MethodDef-");

			ModuleDef MyModuleDef;
			foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
				MyModuleDef = Modnode.GetModule();
				if (MyModuleDef.Name.Contains(Assembly)) {
					if (MyModuleDef.Name.ToString().Contains(Namespace)) {
						Debug.WriteLine("\t" + MyModuleDef.Name);
						DataToReturn += MyModuleDef.Name + "\r\n";
						var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase).ToList();
						foreach (TypeDef MyType in ModTypes) {
							Debug.WriteLine("\t" + MyType.FullName);
							if (MyType.FullName.StartsWith(Namespace + "." + ClassName)) { //+MethodName
								if (!DumpCode) {
									DataToReturn += "\t" + MyType.FullName + "\r\n";
								}
								//.OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
								List<MethodDef> Methods = MyType.Methods.OrderBy(t => t.Name.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
								foreach (MethodDef MyMethod in Methods) {
									if (MyMethod.Name.Contains(MethodName)) {
										//Debug.WriteLine("\t\t" + MyType.FullName);
										if (!DumpCode) {
											//Debug.WriteLine("\t\t\t" + MyMethod.FullName); //The specific Method, Way too much data
											DataToReturn += "\t\t" + MyMethod.FullName + "\r\n";
										}
										if (DumpCode) {
											DataToReturn += TheExtension.DumpSource(Modnode, MyMethod);
										}
									}
								}
							}
						}
					}
				}
			}
			return DataToReturn;
		}

		/// <summary>
		/// Walks *any* ITreeNode subtree, printing out:
		//   • Module.Name (if any)
		//   • Assembly/Document/Module path names
		/// • “MethodDef” if there’s a Method at that node
		/// and recurses into children with increasing indentation.
		/// </summary>
		public string DumpAllNodeClassesAndMethodsOld(ITreeNode node, int indentLevel = 0) {
			var indent = new string('\t', indentLevel);

			ModuleDef moduleDef = node.Data.GetModule();
			//var asmNode = node.Data.GetAssemblyNode();
			//var docNode = node.Data.GetDocumentNode(); //Doc and Mod seem to be the same
			var modNode = node.Data.GetModuleNode();
			//ITreeNode TN = node.Data.TreeNode;
			//if (asmNode != null && docNode != null && modNode != null)
			//	Debug.WriteLine($"{indent}{asmNode.NodePathName} {docNode.NodePathName} {modNode.NodePathName}");

			//Debug.WriteLine(TN.Data.Text);
			//DumpSource(modNode, moduleDef);

			foreach (MemberRef MyRef in moduleDef.GetMemberRefs().ToList()) {
				//Debug.WriteLine(MyRef.FullName + "-" + MyRef.Signature.ToString() + "-" + MyRef.GetParamCount());
			}

			foreach (ModuleRef MyRef in moduleDef.GetModuleRefs().ToList()) {
				//Debug.WriteLine(MyRef.FullName);
			}

			foreach (AssemblyRef MyAsmRef in moduleDef.GetAssemblyRefs().ToList()) {
				//Debug.WriteLine(MyAsmRef.FullName);
			}

			var ModTypes = moduleDef.GetTypes().ToList();
			foreach (TypeDef MyType in ModTypes) {
				//Debug.WriteLine(indent + "\t" + MyType.FullName);
				if (MyType.FullName.StartsWith("CNETTrafficFighterWeb")) {
					if (modNode != null) {
						Debug.WriteLine(indent + "\t" + MyType.FullName); //The Class
																		  //Debug.WriteLine(DumpSource(modNode, MyType)); //The class as a whole
					}
				}

				foreach (MethodDef MyMethod in MyType.Methods) {
					Debug.WriteLine(indent + "\t\t" + MyMethod.FullName); //The specific Method, Way too much data
				}
			}

			//foreach (ITreeNode child in node.Children.ToList()) {
			//	DumpNode(child, indentLevel + 1);
			//	List<ITreeNode> mychildren = child.Descendants().ToList();
			//	//Debug.WriteLine(mychildren.Count);	
			//}
			return "";
		}

		/*
		[Command("Dump.All", MCPCmdDescription = "Dumps all Classes and Methods within those classes by Namespace.")]
		public static string DumpAllNamespaceClassesAndMethods(string NamespaceAssemblyName) {
			string DataToReturn = "";
			Debug.WriteLine("-MethodDef-");

			foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
				if (Modnode.GetModule().Name.ToString().Contains(NamespaceAssemblyName)) {
					Debug.WriteLine("\t" + Modnode.GetModule().Name);
					DataToReturn += Modnode.GetModule().Name + "\r\n";
					var ModNode = Modnode.TreeNode.Data.GetModuleNode();
					var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().ToList();
					//DataToReturn += "\t" + DumpNode(Modnode.TreeNode, 1);

					foreach (TypeDef MyType in ModTypes) {
						//Debug.WriteLine(indent + "\t" + MyType.FullName);
						if (ModNode != null) { //parent module can be located?
							Debug.WriteLine("\t\t" + MyType.FullName); //The Class
							DataToReturn += ("\t" + MyType.FullName + "\r\n");
							//Debug.WriteLine(DumpSource(modNode, MyType)); //The class as a whole
						}
						foreach (MethodDef MyMethod in MyType.Methods) {
							Debug.WriteLine("\t\t\t" + MyMethod.FullName); //The specific Method, Way too much data
							DataToReturn += ("\t\t" + MyType.FullName + "\r\n");
						}
					}
				}
			}
			return DataToReturn;
		}
		*/

	}
}
