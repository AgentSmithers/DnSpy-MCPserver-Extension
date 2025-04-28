using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using dnSpy.Contracts.App;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Extension;
using dnSpy.Contracts.TreeView;
using dnSpy.Contracts.ToolWindows.App;
using dnlib.DotNet;
using dnSpy.Contracts.Text;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using dnSpy.Contracts.Documents.TreeView;
using static Example1.Extension.SimpleMcpServer;
using System.Runtime.Remoting.Contexts;
using dnSpy.Contracts.Documents.Tabs;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

// Each extension should export one class implementing IExtension

namespace Example1.Extension {
	[ExportExtension]

	sealed class TheExtension : IExtension {

		//[Export, Export(typeof(IDocumentTreeView))] 
		[Import] public IAppWindow dnWindow;
		[Import] public ITreeViewService? treeViewService; // Use nullable aware reference types if enabled
		[Import(AllowDefault = true)] public IDsToolWindowService ToolWindowService { get; set; }
		//[Import(AllowDefault = true)] public ToolWindowContent MyToolWindow;
		//[Import(AllowDefault = true)] public IDocumentTreeNodeDataContext ToolWindowContentProvider; //null
		[Import(AllowDefault = true)] public IDocumentTreeView MyTreeView;
		[Import(AllowDefault = true)] public IDocumentTabService MyTabService;

		[Import] public IDecompilerService decompilerService;


		public IEnumerable<string> MergedResourceDictionaries {
			get {
				yield break;
			}
		}

		public ExtensionInfo ExtensionInfo => new ExtensionInfo {
			ShortDescription = "Ability to check for updates on github.",
			Copyright = "Copyright 2019 DeStilleGast (except on Newtonsoft.Json.dll that is included)"
		};

		public static string DumpSource(ModuleDocumentNode Mod, ModuleDef moduleDef) {
			var decCtx = new DecompilationContext();
			var sb = new StringBuilder();
			using (var sw = new StringWriter(sb)) {
				var indenter = new Indenter(4, 4, true);
				var textOutput = new TextWriterDecompilerOutput(sw, indenter);
				Mod.Context.Decompiler.Decompile(moduleDef, textOutput, decCtx);  // :contentReference[oaicite:1]{index=1}
			}
			try {
				Debug.WriteLine(sb.ToString());
				return sb.ToString();
			}
			catch (ExternalException) {
				// swallow
			}
			return null;
		}

		public static string DumpSource(ModuleDocumentNode Mod, MethodDef methodDef) { //MethodDef is a function
			var decCtx = new DecompilationContext();
			var sb = new StringBuilder();
			using (var sw = new StringWriter(sb)) {
				var indenter = new Indenter(4, 4, true);
				var textOutput = new TextWriterDecompilerOutput(sw, indenter);
				Mod.Context.Decompiler.Decompile(methodDef, textOutput, decCtx);  // :contentReference[oaicite:1]{index=1}
			}
			try {
				Debug.WriteLine(sb.ToString());
				return sb.ToString();
			}
			catch (ExternalException) {
				// swallow
			}
			return null;
		}

		public static string DumpSource(ModuleDocumentNode Mod, TypeDef typeDef) { //typeDef is a class
			var decCtx = new DecompilationContext();
			var sb = new StringBuilder();
			using (var sw = new StringWriter(sb)) {
				var indenter = new Indenter(4, 4, true);
				var textOutput = new TextWriterDecompilerOutput(sw, indenter);
				Mod.Context.Decompiler.Decompile(typeDef, textOutput, decCtx);  // :contentReference[oaicite:1]{index=1}
			}
			try {
				Debug.WriteLine(sb.ToString());
				//Clipboard.SetText(sb.ToString());
				return sb.ToString();
			}
			catch (ExternalException) {
				// swallow
			}
			return null;
		}

		public static string UpdateSource(ModuleDocumentNode modNode, MethodDef methodDef, string newCSharpBody) // just the statements inside the MethodDef (Function)
		{
			string source = "";
			try {
				// 1) Build a small C# source wrapper
				//    matching the signature of methodDef:
				var retType = methodDef.ReturnType.FullName;
				var parameters = string.Join(", ", methodDef.Parameters.Where(p => !p.IsHiddenThisParameter).Select(p => p.Type.FullName + " " + p.Name));
				source = $@"using System;
				public static class __Patch {{
					public static {retType} {methodDef.Name}({parameters}) {{
						{newCSharpBody}
					}}
				}}";

				/*
				using System;
				public static class __Patch {
					public static System.String HelloWorld(CNETTrafficFighterWeb.com.myqnapcloud.desertqnap.API ) {
						Console.WriteLine("Hello from patched method!"); return "TestedValue";
					}
				}
				*/

				// 2) Compile with Roslyn into a MemoryStream
				var tree = CSharpSyntaxTree.ParseText(source);
				var refs = new[]
				{
					MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
					MetadataReference.CreateFromFile(modNode.GetModule().Location)
				};
				var comp = CSharpCompilation.Create(
					"__PatchAsm",
					new[] { tree },
					refs,
					new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
				);
				using var ms = new MemoryStream();
				var result = comp.Emit(ms);
				if (!result.Success)
					return "❌ Compilation errors:\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
					.Select(d => d.ToString()));

				// 3) Load compiled bytes with dnlib
				var patchMod = ModuleDefMD.Load(ms.ToArray());
				var patchType = patchMod.Types.First(t => t.Name == "__Patch");
				var patchMethod = patchType.Methods.First(m => m.Name == methodDef.Name);

				// 4) Copy its IL into your target
				var body = methodDef.Body;
				body.Instructions.Clear();
				foreach (var instr in patchMethod.Body.Instructions)
					body.Instructions.Add(instr);

				// 5) Refresh dnSpy’s UI
				Global.MyTreeView.TreeView.RefreshAllNodes();

				return $"✅ Updated method body of {methodDef.Name}";
			}
			catch (Exception ex) {
				return "Exception: Failed to update function\r\n\t\n" + source;
			}
		}

		public void OnEvent(ExtensionEvent @event, object? obj) {
			if (@event == ExtensionEvent.AppLoaded) {
				new Thread(() => {
					Debug.WriteLine("AppLoaded");

					if (true) 
					{
						dnWindow.MainWindow.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() => {
							var askToOpenPage = MsgBox.Instance.Show($"Attach your debugger if you wish, then click okay to start the MCP Server", MsgBoxButton.OK);
							if (askToOpenPage == MsgBoxButton.Yes) {			
							}

							MCPCommands MyMCPCommands = new MCPCommands();
							Global.MySimpleMCPServer = new SimpleMcpServer(MyMCPCommands.GetType());
							Global.MyTreeView = MyTreeView;
							Global.MyAppWindow = dnWindow;
							Global.MyDocumentTabService = MyTabService;
							Global.MySimpleMCPServer.Start();

							string AssemblyName = "CNETTrafficFighterWeb";
							string NamespaceName = "CNETTrafficFighterWeb.com.myqnapcloud.desertqnap";
							string ClassName = "API";
							string FunctionName = "HelloWorld"; //HelloWorld" //TranslateTextToAudio

							bool testing = false;
							if (testing) 
							{
								//MCPCommands.PatchMethodLogEntry(AssemblyName, NamespaceName, ClassName, FunctionName);
								var Opcode = MCPCommands.Get_Function_Opcodes(AssemblyName, NamespaceName, ClassName, FunctionName);

								var snippet = @"Console.WriteLine(""Hello from patched method!""); return ""TestedValue"";";
								//var UpdateSrc = MCPCommands.Update_Methods_Sourcode(AssemblyName, NamespaceName, ClassName, FunctionName, snippet);


								var ilLines = new[] {
									// note: your regex skips the “offset” column, so just supply “OpCode Operand”
									"Ldstr Hello, world!",
									"Call System.Console::WriteLine(System.String)",
									"Ret"
								};

								//Opcode = MCPCommands.Overwrite_Full_Function_Opcodes(AssemblyName, NamespaceName, ClassName, FunctionName, ilLines);
								Opcode = MCPCommands.Set_Function_Opcodes(AssemblyName, NamespaceName, ClassName, FunctionName, ilLines, 15, "Appended");
								//Opcode = MCPCommands.Set_Function_Opcodes(AssemblyName, NamespaceName, ClassName, FunctionName, ilLines, 10, "Overwrite");
								Opcode = MCPCommands.Get_Function_Opcodes(AssemblyName, NamespaceName, ClassName, FunctionName);
								MCPCommands.RefreshAllOpenTabs();
								var Asms = MCPCommands.DumpLoadedAssemblies(); //List all active Assemblys
								var Namespaces = MCPCommands.DumpNamespacesFromAssembly(AssemblyName); //Dumps all Namespaces in an Assembly
								var ClassList = MCPCommands.DumpClassesFromNamespace(AssemblyName, NamespaceName);
								var FunctionPrototypeList = MCPCommands.DumpMethodPrototypes(AssemblyName, NamespaceName, ClassName);
								var ClassSoureCode = MCPCommands.DumpClassCode(AssemblyName, NamespaceName, ClassName);
								var FunctionSourceCode = MCPCommands.DumpMethodsSourcode(AssemblyName, NamespaceName, ClassName, FunctionName);

								var Classes = MCPCommands.DumpClasses(AssemblyName, NamespaceName, "", false); //Dumps all Classes in a Namespace
								var ClassesWithFunctions = MCPCommands.DumpClasses(AssemblyName, NamespaceName, "", true); //Dumps all Classes in a Namespace including their function prototypes
								var ClassWithFunctions = MCPCommands.DumpClasses(AssemblyName, NamespaceName, ClassName, true); //Dumps specific Class functions, Use the Dump Functions command
								var ClassSourceCode = MCPCommands.DumpClasses(AssemblyName, NamespaceName, ClassName, false, true); //Dumps full source for specific class

								var functions = MCPCommands.DumpMethods(AssemblyName, NamespaceName, ClassName, "", false); //Dumps specific Class functions
								var function = MCPCommands.DumpMethods(AssemblyName, NamespaceName, ClassName, FunctionName, true); //Dumps source for specific function
							}

							//MyMCPCommands.DumpAllNamespaceClassesAndFunctions("CNETTrafficFighterWeb");

							if (ToolWindowService != null) {
								//ToolWindowService.Show()
								var asmExplorerGuid = new Guid("5495EE9F-1EF2-45F3-A320-22A89BFDF731");
								var win = ToolWindowService.Show(asmExplorerGuid);
								var ui = win.UIObject as DependencyObject;
								var MySharpeTreeView = win.UIObject as ICSharpCode.TreeView.SharpTreeView;
								var model = (ui as FrameworkElement)?.DataContext;
								//Debug.WriteLine(ui.DependencyObjectType.Name); //Returns "SharpTreeView"
							}
						}));
					}
				}
				).Start();
			}
			else if (@event == ExtensionEvent.Loaded) {
				//AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
			}
		}
	}
}
