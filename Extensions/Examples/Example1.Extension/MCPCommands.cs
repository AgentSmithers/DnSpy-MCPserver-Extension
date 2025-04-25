using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using dnlib.DotNet;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.TreeView;
using static Example1.Extension.SimpleMcpServer;

namespace Example1.Extension
{
    class MCPCommands
    {

		/// <summary>
		/// Walks *any* ITreeNode subtree, printing out:
		//   • Module.Name (if any)
		//   • Assembly/Document/Module path names
		/// • “MethodDef” if there’s a method at that node
		/// and recurses into children with increasing indentation.
		/// </summary>
		public string DumpAllNodeClassesAndFunctionsOld(ITreeNode node, int indentLevel = 0) {
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
					Debug.WriteLine(indent + "\t\t" + MyMethod.FullName); //The specific function, Way too much data
				}
			}

			//foreach (ITreeNode child in node.Children.ToList()) {
			//	DumpNode(child, indentLevel + 1);
			//	List<ITreeNode> mychildren = child.Descendants().ToList();
			//	//Debug.WriteLine(mychildren.Count);	
			//}
			return "";
		}

		[Command("DumpAllNamespaceClassesAndFunctions", MCPCmdDescription = "Dumps all Classes and Functions within those classes by Namespace.")]
		public static string DumpAllNamespaceClassesAndFunctions(string NamespaceAssemblyName) {
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
							Debug.WriteLine("\t\t\t" + MyMethod.FullName); //The specific function, Way too much data
							DataToReturn += ("\t\t" + MyType.FullName + "\r\n");
						}
					}
				}
			}
			return DataToReturn;
		}

		[Command("DumpParentNodes")]
		public static string DumpParentNodes() {
			string DataToReturn = "";
			Debug.WriteLine("-ModuleDocumentNode-");
			ModuleDef MyModule;
			foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
				MyModule = Modnode.GetModule();
				Debug.WriteLine("\t" + MyModule.Name + "->" + MyModule.Assembly.Name + " Location: " + MyModule.Location);
				DataToReturn += MyModule.Assembly.Name + "\r\n";
			}

			//var DocNodes = MyTreeView.GetAllCreatedDocumentNodes().ToList(); //This seems to repeat every library twice?
			//Debug.WriteLine("-DsDocumentNodes-");
			//foreach (DsDocumentNode MyNode in DocNodes) {
			//	Debug.WriteLine(MyNode.GetModule().Name);
			//	if (MyNode.GetModule().Name.ToString().StartsWith("CNETTrafficFighterWeb")) {
			//		//DumpNode(MyNode.TreeNode, 0);
			//	}
			//}

			//foreach (DocumentTreeNodeData MyNode in MyTreeView.FindNode(null) {
			//	if (MyNode.GetModule().Name.ToString().StartsWith("CNETTrafficFighterWeb")) {
			//		DumpNode(MyNode.TreeNode, 0);
			//	}
			//}
			return DataToReturn;
		}

		[Command("DumpNameSpaceNodes", MCPCmdDescription = "Dumps all Classes by Namespace.")]
		public static string DumpNameSpaceNodes(string Namespace) { //"CNETTrafficFighterWeb"
			string DataToReturn = "";
			Debug.WriteLine("-ModuleDocumentNodes-");
			ModuleDef MyNamespace;
			foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
				MyNamespace = Modnode.GetModule();
				Debug.WriteLine("\t" + MyNamespace.Name);
				DataToReturn += MyNamespace.Name + "\r\n";
				if (MyNamespace.Name.ToString().StartsWith(Namespace)) {
					var ModNode = Modnode.TreeNode.Data.GetModuleNode();
					var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().ToList();
					foreach (TypeDef MyType in ModTypes) {
						if (MyType.FullName.StartsWith(Namespace)) {
							if (ModNode != null) {
								Debug.WriteLine("\t\t" + MyType.FullName); //The Class
								DataToReturn += "\t" + MyType.FullName + "\r\n";
								//Debug.WriteLine(DumpSource(modNode, MyType)); //The class as a whole
							}
						}
					}
				}
			}
			return DataToReturn;
		}

		[Command("DumpClassNodes", MCPCmdDescription = "Dumps all Functions by Class within a given Namespace. Set 'DumpCode' = true to view the code within the Class as a whole.")]
		public static string DumpClassNodes(string Namespace, string ClassName, bool DumpCode = false) { //Dumps a Class and its functions
			string DataToReturn = "";
			//Debug.WriteLine("-MethodDef-");

			foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
				if (Modnode.GetModule().Name.ToString().Contains(Namespace)) {
					//Debug.WriteLine("\t" + Modnode.GetModule().Name);
					DataToReturn += Modnode.GetModule().Name + "\r\n";

					var ModNode = Modnode.TreeNode.Data.GetModuleNode();
					var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().ToList();
					//DataToReturn += "\t" + DumpNode(Modnode.TreeNode, 1);
					foreach (TypeDef MyType in ModTypes) {
						//Debug.WriteLine(indent + "\t" + MyType.FullName);
						if (MyType.FullName.StartsWith(Namespace + "." + ClassName)) { //+FunctionName
							if (!DumpCode) {
								if (ModNode != null) {
									//Debug.WriteLine("\t\t" + MyType.FullName); //The Class
									DataToReturn += "\t" + MyType.FullName + "\r\n";
									//Debug.WriteLine(DumpSource(modNode, MyType)); //The class as a whole
								}
								foreach (MethodDef MyMethod in MyType.Methods) {
									//Debug.WriteLine("\t\t\t" + MyMethod.FullName); //The specific function, Way too much data
									DataToReturn += "\t\t" + MyMethod.FullName + "\r\n";
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
			return DataToReturn;
		}

		[Command("DumpFunction", MCPCmdDescription = "Dumps all Functions by Class within a given Namespace. Set 'DumpCode' = true to view the code within the Class as a whole.")]
		public static string DumpFunction(string Namespace, string ClassName, string FunctionName, bool DumpCode = false) {
			string DataToReturn = "";
			//Debug.WriteLine("-MethodDef-");

			foreach (ModuleDocumentNode Modnode in Global.MyTreeView.GetAllModuleNodes().ToList()) {
				if (Modnode.GetModule().Name.ToString().Contains(Namespace)) {
					//Debug.WriteLine("\t" + Modnode.GetModule().Name);
					DataToReturn += Modnode.GetModule().Name + "\r\n";
					var ModTypes = Modnode.TreeNode.Data.GetModule().GetTypes().ToList();
					foreach (TypeDef MyType in ModTypes) {
						if (MyType.FullName.StartsWith(Namespace + "." + ClassName)) { //+FunctionName
							foreach (MethodDef MyMethod in MyType.Methods) {
								if (MyMethod.Name.Contains(FunctionName)) {
									//Debug.WriteLine("\t\t" + MyType.FullName);
									if (!DumpCode) {
										DataToReturn += "\t" + MyType.FullName + "\r\n";
										//Debug.WriteLine("\t\t\t" + MyMethod.FullName); //The specific function, Way too much data
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
			return DataToReturn;
		}
	}
}
