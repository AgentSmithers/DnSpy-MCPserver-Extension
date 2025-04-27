using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using dnSpy.Contracts.App;
using dnSpy.Contracts.Controls;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Extension;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.Settings.Dialog;
using dnSpy.Contracts.TreeView;
using dnSpy.Contracts.ToolWindows;
using System.ComponentModel.Design;
using dnSpy.Contracts.ToolWindows.App;
using dnSpy.Contracts.Menus;
using Microsoft.VisualStudio.Utilities;
using System.Windows.Media;
using ICSharpCode.TreeView;
using System.Diagnostics.Contracts;
using System.Windows.Media.TextFormatting;
using System.Windows.Controls;
using System.Reflection;
using dnlib.DotNet;
using dnSpy.Contracts.Text;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using dnSpy.Contracts.Documents.TreeView;
using System.Windows.Documents;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using System.ComponentModel;
using System.Linq;
using System.Web.UI.WebControls;
using static Example1.Extension.SimpleMcpServer;

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

		public static string DumpSource(ModuleDocumentNode Mod, ModuleDef methodDef) {
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

		public static string DumpSource(ModuleDocumentNode Mod, TypeDef typeDef) {
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

		public static string DumpSource(ModuleDocumentNode Mod, MethodDef methodDef) {
			var decCtx = new DecompilationContext();
			var sb = new StringBuilder();
			using (var sw = new StringWriter(sb)) {
				var indenter = new Indenter(4, 4, true);
				var textOutput = new TextWriterDecompilerOutput(sw, indenter);
				Mod.Context.Decompiler.Decompile(methodDef, textOutput, decCtx);  // :contentReference[oaicite:1]{index=1}
			}
			try {
				//Debug.WriteLine(sb.ToString());
				//Clipboard.SetText(sb.ToString());
				return sb.ToString();
			}
			catch (ExternalException) {
				// swallow
			}
			return null;
		}

		public void OnEvent(ExtensionEvent @event, object? obj) {
			if (@event == ExtensionEvent.AppLoaded) {
				new Thread(() => {
					Debug.WriteLine("AppLoaded");

					MCPCommands MyMCPCommands = new MCPCommands();
					
					if (true) 
					{
						dnWindow.MainWindow.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() => {
							var askToOpenPage = MsgBox.Instance.Show($"Attach your debugger if you wish, then click okay to start the MCP Server", MsgBoxButton.OK);
							if (askToOpenPage == MsgBoxButton.Yes) {			
							}

							
							Global.MySimpleMCPServer = new SimpleMcpServer(MyMCPCommands.GetType());
							Global.MyTreeView = MyTreeView;
							Global.MySimpleMCPServer.Start();

							string AssemblyName = "CNETTrafficFighterWeb";
							string NamespaceName = "CNETTrafficFighterWeb.com.myqnapcloud.desertqnap";
							string ClassName = "API";
							string FunctionName = "HelloWorld";

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
