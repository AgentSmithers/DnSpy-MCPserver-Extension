/*
    Copyright (C) 2014-2019 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Windows;
using System.Windows.Controls;
using System.Text;

namespace dnSpy.AsmEditor.MethodBody {
sealed partial class MethodBodyControl : UserControl {
    LocalsListHelper? localsListHelper;
    InstructionsListHelper? instructionsListHelper;
    ExceptionHandlersListHelper? exceptionHandlersListHelper;

    public MethodBodyControl()
    {
        InitializeComponent();
        DataContextChanged += MethodBodyControl_DataContextChanged;
        Loaded += MethodBodyControl_Loaded;
    }

    void MethodBodyControl_Loaded ( object? sender, RoutedEventArgs e )
    {
        Loaded -= MethodBodyControl_Loaded;
        SetFocusToControl();
    }

    void SetFocusToControl()
    {
        var data = DataContext as MethodBodyVM;
        if ( data is null )
        { return; }

        if ( data.IsCilBody )
        { instructionsListView.Focus(); }
        else
        { rvaTextBox.Focus(); }
    }

    void MethodBodyControl_DataContextChanged ( object? sender, DependencyPropertyChangedEventArgs e )
    {
        var data = DataContext as MethodBodyVM;
        if ( data is null )
        { return; }

        var ownerWindow = Window.GetWindow ( this );
        localsListHelper = new LocalsListHelper ( localsListView, ownerWindow );
        instructionsListHelper = new InstructionsListHelper ( instructionsListView, ownerWindow );
        exceptionHandlersListHelper = new ExceptionHandlersListHelper ( ehListView, ownerWindow );

        localsListHelper.OnDataContextChanged ( data );
        instructionsListHelper.OnDataContextChanged ( data );
        exceptionHandlersListHelper.OnDataContextChanged ( data );
    }

    void CopyAllInstructions_Click ( object sender, RoutedEventArgs e )
    {
        var data = DataContext as MethodBodyVM;
        if ( data?.CilBodyVM?.InstructionsListVM is not { } instructions )
            return;

        var sb = new System.Text.StringBuilder();
        foreach (var instructionVm in instructions)
        {
            // Get the actual dnlib.DotNet.Emit.Instruction object
            // This object's ToString() method should provide the correct formatted instruction string
            var dnlibInstruction = instructionVm.GetTempInstruction();

            string opcodeName = dnlibInstruction.OpCode.Name;
            string operandString = string.Empty;

            if (dnlibInstruction.Operand != null)
            {
                // Handle specific operand types that might format themselves with "IL_XXXX:"
                if (dnlibInstruction.Operand is dnlib.DotNet.Emit.Instruction targetInstruction)
                {
                    // For branch targets, we want "IL_XXXX" format, not the full instruction string
                    operandString = $"IL_{targetInstruction.Offset:X4}";
                }
                else
                {
                    // For other operand types, use their default ToString()
                    operandString = dnlibInstruction.Operand.ToString();
                }
            }

            string instructionPart = opcodeName;
            if (!string.IsNullOrEmpty(operandString))
            {
                instructionPart += " " + operandString;
            }

            sb.AppendFormat("IL_{0:X4}: {1:X4} {2}", instructionVm.Offset, instructionVm.Index, instructionPart);
            sb.AppendLine();
        }
        Clipboard.SetText(sb.ToString());
    }
}
}
