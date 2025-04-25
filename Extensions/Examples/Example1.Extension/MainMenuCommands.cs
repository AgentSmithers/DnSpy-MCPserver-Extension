using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using dnlib.DotNet;
using dnlib.PE;
using dnSpy.Contracts.App;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.Menus;
using dnSpy.Contracts.TreeView;
using Microsoft.VisualStudio.Text.Editor;

// Adds a new "_Extension" menu and several commands.
// Adds a command to the View menu

namespace Example1.Extension {
	static class MainMenuConstants {
		//TODO: Use your own guids
		public const string APP_MENU_EXTENSION = "4E6829A6-AEA0-4803-9344-D19BF0A74DA1";
		public const string GROUP_EXTENSION_MENU1 = "0,73BEBC37-387A-4004-8076-A1A90A17611B";
		public const string GROUP_EXTENSION_MENU2 = "10,C21B8B99-A2E4-474F-B4BC-4CF348ECBD0A";
	}

	

	// Create the Extension menu and place it right after the Debug menu
	[ExportMenu(OwnerGuid = MenuConstants.APP_MENU_GUID, Guid = MainMenuConstants.APP_MENU_EXTENSION, Order = MenuConstants.ORDER_APP_MENU_DEBUG + 0.1, Header = "_Extension")]
	sealed class DebugMenu : IMenu {
	}

	[ExportMenuItem(OwnerGuid = MainMenuConstants.APP_MENU_EXTENSION, Header = "Command #1", Group = MainMenuConstants.GROUP_EXTENSION_MENU1, Order = 0)]
	sealed class ExtensionCommand1 : MenuItemBase {
		public override void Execute(IMenuItemContext context) => MsgBox.Instance.Show("Command #1");
	}

	[ExportMenuItem(OwnerGuid = MainMenuConstants.APP_MENU_EXTENSION, Header = "Command #2", Group = MainMenuConstants.GROUP_EXTENSION_MENU1, Order = 10)]
	sealed class ExtensionCommand2 : MenuItemBase {

		static IDsDocument? GetDocument(TreeNodeData node) {
			var fileNode = node as DsDocumentNode;
			if (fileNode is null)
				return null;

			var peImage = fileNode.Document.PEImage;
			if (peImage is null)
				peImage = (fileNode.Document.ModuleDef as ModuleDefMD)?.Metadata?.PEImage;

			return (peImage as IInternalPEImage)?.IsMemoryMappedIO == true ? fileNode.Document : null;
		}
		public override void Execute(IMenuItemContext context) {
			dnSpy.Contracts.Documents.DsDocument activeDocumentService = null;
			var activeTextView = activeDocumentService;
			var docViewer = context.Find<IDocumentViewer>();
			var reference = context.Find<TextReference>();

			if (context.CreatorObject.Guid != new Guid(MenuConstants.GUIDOBJ_DOCUMENTS_TREEVIEW_GUID))
				return;
			var asms = new List<IDsDocument>();
			foreach (var node in (context.Find<TreeNodeData[]>() ?? Array.Empty<TreeNodeData>())) {
				var file = GetDocument(node);
			}
			foreach (var asm in asms)
				(asm.PEImage as IInternalPEImage)?.UnsafeDisableMemoryMappedIO();
		}
	}

	[ExportMenuItem(OwnerGuid = MainMenuConstants.APP_MENU_EXTENSION, Header = "Command #3", Group = MainMenuConstants.GROUP_EXTENSION_MENU2, Order = 0)]
	sealed class ExtensionCommand3 : MenuItemBase {
		public override void Execute(IMenuItemContext context) => MsgBox.Instance.Show("Command #3");
	}

	[ExportMenuItem(OwnerGuid = MainMenuConstants.APP_MENU_EXTENSION, Header = "Command #4", Group = MainMenuConstants.GROUP_EXTENSION_MENU2, Order = 10)]
	sealed class ExtensionCommand4 : MenuItemBase {
		public override void Execute(IMenuItemContext context) => MsgBox.Instance.Show("Command #4");
	}

	[ExportMenuItem(OwnerGuid = MainMenuConstants.APP_MENU_EXTENSION, Header = "Command #5", Group = MainMenuConstants.GROUP_EXTENSION_MENU2, Order = 20)]
	sealed class ExtensionCommand5 : MenuItemBase {
		public override void Execute(IMenuItemContext context) => MsgBox.Instance.Show("Command #5");
	}

	[ExportMenuItem(OwnerGuid = MenuConstants.APP_MENU_VIEW_GUID, Header = "Command #1", Group = MenuConstants.GROUP_APP_MENU_VIEW_WINDOWS, Order = 1000)]
	sealed class ViewCommand1 : MenuItemBase {
		public override void Execute(IMenuItemContext context) => MsgBox.Instance.Show("View Command #1");
	}
}
