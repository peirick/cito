// GenJs.cs - JavaScript code generator
//
// Copyright (C) 2011-2022  Piotr Fusik
//
// This file is part of CiTo, see https://github.com/pfusik/cito
//
// CiTo is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// CiTo is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with CiTo.  If not, see http://www.gnu.org/licenses/

using System;
using System.Collections.Generic;
using System.Linq;

namespace Foxoft.Ci
{

enum GenJsMethod
{
	CopyArray,
	SortListPart,
	RegexEscape,
	Count,
	UInt32
}

public class GenJs : GenBase
{
	// TODO: Namespace
	protected readonly string[][] Library = new string[(int) GenJsMethod.Count][];

	void WriteCamelCaseNotKeyword(string name)
	{
		switch (name) {
		case "catch":
		case "debugger":
		case "delete":
		case "export":
		case "extends":
		case "finally":
		case "function":
		case "implements":
		case "import":
		case "instanceof":
		case "interface":
		case "let":
		case "package":
		case "private":
		case "super":
		case "try":
		case "typeof":
		case "var":
		case "with":
		case "yield":
			Write(name);
			Write('_');
			break;
		default:
			WriteCamelCase(name);
			break;
		}
	}

	protected override void WriteName(CiSymbol symbol)
	{
		switch (symbol) {
		case CiContainerType _:
			Write(symbol.Name);
			break;
		case CiConst konst:
			if (konst.InMethod != null) {
				WriteUppercaseWithUnderscores(konst.InMethod.Name);
				Write('_');
			}
			WriteUppercaseWithUnderscores(symbol.Name);
			break;
		case CiVar _:
		case CiMember _:
			WriteCamelCaseNotKeyword(symbol.Name);
			break;
		default:
			throw new NotImplementedException(symbol.GetType().Name);
		}
	}

	protected override void WriteTypeAndName(CiNamedValue value)
	{
		WriteName(value);
	}

	protected static string GetArrayElementType(CiNumericType type)
	{
		if (type == CiSystem.IntType)
			return "Int32";
		if (type == CiSystem.DoubleType)
			return "Float64";
		if (type == CiSystem.FloatType)
			return "Float32";
		if (type == CiSystem.LongType)
			// TODO: UInt32 if possible?
			return "Float64"; // no 64-bit integers in JavaScript
		CiRangeType range = (CiRangeType) type;
		if (range.Min < 0) {
			if (range.Min < short.MinValue || range.Max > short.MaxValue)
				return "Int32";
			if (range.Min < sbyte.MinValue || range.Max > sbyte.MaxValue)
				return "Int16";
			return "Int8";
		}
		if (range.Max > ushort.MaxValue)
			return "Int32";
		if (range.Max > byte.MaxValue)
			return "Uint16";
		return "Uint8";
	}

	public override CiExpr Visit(CiAggregateInitializer expr, CiPriority parent)
	{
		CiNumericType number = ((CiArrayStorageType) expr.Type).ElementType as CiNumericType;
		if (number != null) {
			Write("new ");
			Write(GetArrayElementType(number));
			Write("Array(");
		}
		Write("[ ");
		WriteCoercedLiterals(null, expr.Items);
		Write(" ]");
		if (number != null)
			Write(')');
		return expr;
	}

	protected override void WriteNewStorage(CiType type)
	{
		if (type is CiListType || type is CiStackType)
			Write("[]");
		else if (type is CiHashSetType)
			Write("new Set()");
		else if (type is CiDictionaryType)
			Write("{}");
		else
			base.WriteNewStorage(type);
	}

	protected override void WriteVar(CiNamedValue def)
	{
		Write(def.Type.IsFinal && !def.IsAssignableStorage ? "const " : "let ");
		base.WriteVar(def);
	}

	void WriteInterpolatedLiteral(string s)
	{
		for (int i = 0; i < s.Length; i++) {
			char c = s[i];
			if (c == '`'
			 || (c == '$' && i + 1 < s.Length && s[i + 1] == '{'))
				Write('\\');
			Write(c);
		}
	}

	public override CiExpr Visit(CiInterpolatedString expr, CiPriority parent)
	{
		Write('`');
		foreach (CiInterpolatedPart part in expr.Parts) {
			WriteInterpolatedLiteral(part.Prefix);
			Write("${");
			if (part.Width != 0 || part.Format != ' ') {
				part.Argument.Accept(this, CiPriority.Primary);
				if (part.Argument.Type is CiNumericType) {
					switch (part.Format) {
					case 'E':
						Write(".toExponential(");
						if (part.Precision >= 0)
							VisitLiteralLong(part.Precision);
						Write(").toUpperCase()");
						break;
					case 'e':
						Write(".toExponential(");
						if (part.Precision >= 0)
							VisitLiteralLong(part.Precision);
						Write(')');
						break;
					case 'F':
					case 'f':
						Write(".toFixed(");
						if (part.Precision >= 0)
							VisitLiteralLong(part.Precision);
						Write(')');
						break;
					case 'X':
						Write(".toString(16).toUpperCase()");
						break;
					case 'x':
						Write(".toString(16)");
						break;
					default:
						Write(".toString()");
						break;
					}
					if (part.Precision >= 0 && "DdXx".IndexOf(part.Format) >= 0) {
						Write(".padStart(");
						VisitLiteralLong(part.Precision);
						Write(", \"0\")");
					}
				}
				if (part.Width > 0) {
					Write(".padStart(");
					VisitLiteralLong(part.Width);
					Write(')');
				}
				else if (part.Width < 0) {
					Write(".padEnd(");
					VisitLiteralLong(-part.Width);
					Write(')');
				}
			}
			else
				part.Argument.Accept(this, CiPriority.Argument);
			Write('}');
		}
		WriteInterpolatedLiteral(expr.Suffix);
		Write('`');
		return expr;
	}

	protected override void WriteLocalName(CiSymbol symbol, CiPriority parent)
	{
		if (symbol is CiMember member) {
			if (!member.IsStatic)
				Write("this");
			else if (this.CurrentMethod != null)
				Write(this.CurrentMethod.Parent.Name);
			else if (symbol is CiConst konst) {
				konst.Value.Accept(this, parent);
				return;
			}
			else
				throw new NotImplementedException(symbol.ToString());
			Write('.');
		}
		WriteName(symbol);
	}

	protected override void WriteNewArray(CiType elementType, CiExpr lengthExpr, CiPriority parent)
	{
		Write("new ");
		if (elementType is CiNumericType numeric)
			Write(GetArrayElementType(numeric));
		WriteCall("Array", lengthExpr);
	}

	bool HasInitCode(CiNamedValue def)
	{
		return def.Type is CiArrayStorageType array
			&& (array.ElementType is CiClass || array.ElementType is CiArrayStorageType);
	}

	protected override void WriteInitCode(CiNamedValue def)
	{
		if (!HasInitCode(def))
			return;
		CiArrayStorageType array = (CiArrayStorageType) def.Type;
		int nesting = 0;
		while (array.ElementType is CiArrayStorageType innerArray) {
			OpenLoop("let", nesting++, array.Length);
			WriteArrayElement(def, nesting);
			Write(" = ");
			WriteNewArray(innerArray.ElementType, innerArray.LengthExpr, CiPriority.Argument);
			WriteLine(';');
			array = innerArray;
		}
		if (array.ElementType is CiClass klass) {
			OpenLoop("let", nesting++, array.Length);
			WriteArrayElement(def, nesting);
			Write(" = ");
			WriteNew(klass, CiPriority.Argument);
			WriteLine(';');
		}
		while (--nesting >= 0)
			CloseBlock();
	}

	protected override void WriteResource(string name, int length)
	{
		if (length >= 0) // reference as opposed to definition
			Write("Ci.");
		foreach (char c in name)
			Write(CiLexer.IsLetterOrDigit(c) ? c : '_');
	}

	public override CiExpr Visit(CiSymbolReference expr, CiPriority parent)
	{
		if (expr.Symbol == CiSystem.CollectionCount) {
			switch (expr.Left.Type) {
			case CiListType _:
			case CiStackType _:
				expr.Left.Accept(this, CiPriority.Primary);
				Write(".length");
				break;
			case CiHashSetType _:
				expr.Left.Accept(this, CiPriority.Primary);
				Write(".size");
				break;
			case CiDictionaryType _:
				WriteCall("Object.keys", expr.Left);
				Write(".length");
				break;
			default:
				throw new NotImplementedException(expr.Left.Type.ToString());
			}
		}
		else if (expr.Symbol == CiSystem.MatchStart) {
			expr.Left.Accept(this, CiPriority.Primary);
			Write(".index");
		}
		else if (expr.Symbol == CiSystem.MatchEnd) {
			if (parent > CiPriority.Add)
				Write('(');
			expr.Left.Accept(this, CiPriority.Primary);
			Write(".index + ");
			expr.Left.Accept(this, CiPriority.Primary); // FIXME: side effect
			Write("[0].length");
			if (parent > CiPriority.Add)
				Write(')');
		}
		else if (expr.Symbol == CiSystem.MatchLength) {
			expr.Left.Accept(this, CiPriority.Primary);
			Write("[0].length");
		}
		else if (expr.Symbol == CiSystem.MatchValue) {
			expr.Left.Accept(this, CiPriority.Primary);
			Write("[0]");
		}
		else if (expr.Left != null && expr.Left.IsReferenceTo(CiSystem.MathClass)) {
			Write(expr.Symbol == CiSystem.MathNaN ? "NaN"
				: expr.Symbol == CiSystem.MathNegativeInfinity ? "-Infinity"
				: expr.Symbol == CiSystem.MathPositiveInfinity ? "Infinity"
				: throw new NotImplementedException(expr.ToString()));
		}
		else
			return base.Visit(expr, parent);
		return expr;
	}

	protected override void WriteStringLength(CiExpr expr)
	{
		expr.Accept(this, CiPriority.Primary);
		Write(".length");
	}

	protected override void WriteCharAt(CiBinaryExpr expr)
	{
		WriteCall(expr.Left, "charCodeAt", expr.Right);
	}

	void AddLibrary(GenJsMethod id, params string[] method)
	{
		this.Library[(int) id] ??= method;
	}

	static bool IsIdentifier(string s)
	{
		return s.Length > 0
			&& s[0] >= 'A'
			&& s.All(c => CiLexer.IsLetterOrDigit(c));
	}

	void WriteRegex(CiExpr[] args, int argIndex)
	{
		CiExpr pattern = args[argIndex];
		if (pattern.Type.IsClass(CiSystem.RegexClass))
			pattern.Accept(this, CiPriority.Primary);
		else if (pattern is CiLiteralString literal) {
			Write('/');
			bool escaped = false;
			foreach (char c in literal.Value) {
				switch (c) {
				case '\\':
					if (!escaped) {
						escaped = true;
						continue;
					}
					escaped = false;
					break;
				case '"':
				case '\'':
					escaped = false;
					break;
				case '/':
					escaped = true;
					break;
				default:
					break;
				}
				if (escaped) {
					Write('\\');
					escaped = false;
				}
				Write(c);
			}
			Write('/');
			WriteRegexOptions(args, "", "", "", "i", "m", "s");
		}
		else {
			Write("new RegExp(");
			pattern.Accept(this, CiPriority.Argument);
			WriteRegexOptions(args, ", \"", "", "\"", "i", "m", "s");
			Write(')');
		}
	}

	protected override void WriteCall(CiExpr obj, CiMethod method, CiExpr[] args, CiPriority parent)
	{
		if (obj == null) {
			WriteLocalName(method, CiPriority.Primary);
			WriteArgsInParentheses(method, args);
		}
		else if (method == CiSystem.StringSubstring) {
			obj.Accept(this, CiPriority.Primary);
			Write(".substring(");
			args[0].Accept(this, CiPriority.Argument);
			if (args.Length == 2) {
				Write(", ");
				WriteAdd(args[0], args[1]); // TODO: side effect
			}
			Write(')');
		}
		else if (obj.Type is CiArrayType && method.Name == "CopyTo") {
			AddLibrary(GenJsMethod.CopyArray,
				"copyArray : function(sa, soffset, da, doffset, length)",
				"if (typeof(sa.subarray) == \"function\" && typeof(da.set) == \"function\")",
				"\tda.set(sa.subarray(soffset, soffset + length), doffset);",
				"else",
				"\tfor (let i = 0; i < length; i++)",
				"\t\tda[doffset + i] = sa[soffset + i];");
			Write("Ci.copyArray(");
			obj.Accept(this, CiPriority.Argument);
			Write(", ");
			WriteArgs(method, args);
			Write(')');
		}
		else if (obj.Type is CiArrayType array && method.Name == "Fill") {
			obj.Accept(this, CiPriority.Primary);
			Write(".fill(");
			args[0].Accept(this, CiPriority.Argument);
			if (args.Length == 3) {
				Write(", ");
				WriteStartEnd(args[1], args[2]);
			}
			Write(')');
		}
		else if ((obj.Type is CiListType || obj.Type is CiStackType) && method == CiSystem.CollectionClear) {
			obj.Accept(this, CiPriority.Primary);
			Write(".length = 0");
		}
		else if (obj.Type is CiListType && method == CiSystem.CollectionSortAll) {
			obj.Accept(this, CiPriority.Primary);
			Write(".sort((a, b) => a - b)");
		}
		else if (method == CiSystem.CollectionSortPart) {
			if (obj.Type is CiListType) {
				AddLibrary(GenJsMethod.SortListPart,
					"sortListPart: function(a, offset, length)",
					"const sorted = a.slice(offset, offset + length).sort((a, b) => a - b);",
					"for (let i = 0; i < length; i++)",
					"\ta[offset + i] = sorted[i];");
				WriteCall("Ci.sortListPart", obj, args[0], args[1]);
			}
			else {
				obj.Accept(this, CiPriority.Primary);
				Write(".subarray(");
				WriteStartEnd(args[0], args[1]);
				Write(").sort()");
			}
		}
		else if (WriteListAddInsert(obj, method, args, "push", "splice", ", 0, ")
			|| WriteDictionaryAdd(obj, method, args)) {
			// done
		}
		else if (method == CiSystem.ListRemoveAt) {
			obj.Accept(this, CiPriority.Primary);
			Write(".splice(");
			args[0].Accept(this, CiPriority.Argument);
			Write(", 1)");
		}
		else if (method == CiSystem.ListRemoveRange)
			WriteCall(obj, "splice", args[0], args[1]);
		else if (obj.Type is CiDictionaryType && method.Name == "Remove") {
			Write("delete ");
			WriteIndexing(obj, args[0]);
		}
		else if (obj.Type is CiStackType && method.Name == "Peek") {
			obj.Accept(this, CiPriority.Primary);
			Write(".at(-1)");
		}
		else if (obj.Type is CiDictionaryType && method == CiSystem.CollectionClear) {
			Write("for (const key in ");
			obj.Accept(this, CiPriority.Argument);
			WriteLine(')');
			Write("\tdelete ");
			obj.Accept(this, CiPriority.Primary); // FIXME: side effect
			Write("[key];");
		}
		else if (method == CiSystem.ConsoleWrite || method == CiSystem.ConsoleWriteLine) {
			// XXX: Console.Write same as Console.WriteLine
			Write(obj.IsReferenceTo(CiSystem.ConsoleError) ? "console.error" : "console.log");
			if (args.Length == 0)
				Write("(\"\")");
			else
				WriteArgsInParentheses(method, args);
		}
		else if (method == CiSystem.UTF8GetByteCount) {
			Write("new TextEncoder().encode(");
			args[0].Accept(this, CiPriority.Argument);
			Write(").length");
		}
		else if (method == CiSystem.UTF8GetBytes) {
			Write("new TextEncoder().encodeInto(");
			args[0].Accept(this, CiPriority.Argument);
			Write(", ");
			if (args[2].IsLiteralZero)
				args[1].Accept(this, CiPriority.Argument);
			else {
				args[1].Accept(this, CiPriority.Primary);
				Write(".subarray(");
				args[2].Accept(this, CiPriority.Argument);
				Write(')');
			}
			Write(')');
		}
		else if (method == CiSystem.UTF8GetString) {
			Write("new TextDecoder().decode(");
			args[0].Accept(this, CiPriority.Primary);
			Write(".subarray(");
			args[1].Accept(this, CiPriority.Argument);
			Write(", ");
			WriteAdd(args[1], args[2]); // FIXME: side effect
			Write("))");
		}
		else if (method == CiSystem.EnvironmentGetEnvironmentVariable) {
			if (args[0] is CiLiteralString literal && IsIdentifier(literal.Value)) {
				Write("process.env.");
				Write(literal.Value);
			}
			else {
				Write("process.env[");
				args[0].Accept(this, CiPriority.Argument);
				Write(']');
			}
		}
		else if (method == CiSystem.RegexCompile)
			WriteRegex(args, 0);
		else if (method == CiSystem.RegexEscape) {
			AddLibrary(GenJsMethod.RegexEscape,
				"regexEscape : function(s)",
				"return s.replace(/[-\\/\\\\^$*+?.()|[\\]{}]/g, '\\\\$&');");
			Write("Ci.regexEscape");
			WriteArgsInParentheses(method, args);
		}
		else if (method == CiSystem.RegexIsMatchStr) {
			WriteRegex(args, 1);
			WriteCall(".test", args[0]);
		}
		else if (method == CiSystem.RegexIsMatchRegex)
			WriteCall(obj, "test", args[0]);
		else if (method == CiSystem.MatchFindStr || method == CiSystem.MatchFindRegex) {
			if (parent > CiPriority.Equality)
				Write('(');
			Write('(');
			obj.Accept(this, CiPriority.Assign);
			Write(" = ");
			WriteRegex(args, 1);
			WriteCall(".exec", args[0]);
			Write(") != null");
			if (parent > CiPriority.Equality)
				Write(')');
		}
		else if (method == CiSystem.MatchGetCapture)
			WriteIndexing(obj, args[0]);
		else if (method == CiSystem.MathFusedMultiplyAdd) {
			if (parent > CiPriority.Add)
				Write('(');
			args[0].Accept(this, CiPriority.Mul);
			Write(" * ");
			args[1].Accept(this, CiPriority.Mul);
			Write(" + ");
			args[2].Accept(this, CiPriority.Add);
			if (parent > CiPriority.Add)
				Write(')');
		}
		else if (method == CiSystem.MathIsFinite || method == CiSystem.MathIsNaN) {
			WriteCamelCase(method.Name);
			WriteArgsInParentheses(method, args);
		}
		else if (method == CiSystem.MathIsInfinity) {
			if (parent > CiPriority.Equality)
				Write('(');
			WriteCall("Math.abs", args[0]);
			Write(" == Infinity");
			if (parent > CiPriority.Equality)
				Write(')');
		}
		else if (obj.IsReferenceTo(CiSystem.BasePtr)) {
			//TODO: with "class" syntax: Write("super");
			WriteName(method.Parent);
			Write(".prototype.");
			WriteName(method);
			Write(".call(this");
			if (args.Length > 0) {
				Write(", ");
				WriteArgs(method, args);
			}
			Write(')');
		}
		else {
			obj.Accept(this, CiPriority.Primary);
			Write('.');
			if (method == CiSystem.MathCeiling)
				Write("ceil");
			else if (method == CiSystem.MathTruncate)
				Write("trunc");
			else if (method == CiSystem.StringContains || (obj.Type is CiListType && method.Name == "Contains"))
				Write("includes");
			else if (obj.Type is CiHashSetType && method.Name == "Contains")
				Write("has");
			else if (obj.Type is CiHashSetType && method.Name == "Remove")
				Write("delete");
			else if (obj.Type is CiDictionaryType && method.Name == "ContainsKey")
				Write("hasOwnProperty");
			else
				WriteName(method);
			WriteArgsInParentheses(method, args);
		}
	}

	protected override string IsOperator => " instanceof ";

	private CiBinaryExpr WriteIntegerOp(CiBinaryExpr expr, bool parentheses, string op)
	{
		if (parentheses)
			Write('(');
		expr.Left.Accept(this, CiPriority.Mul);
		Write(op);
		expr.Right.Accept(this, CiPriority.Primary);
		if (GetTypeCode(expr.Right.Type, false) == TypeCode.UInt32 ||
		    GetTypeCode(expr.Right.Type, false) == TypeCode.UInt64) {
			Write(" >>> 0"); // FIXME: ulong?
		} else {
			Write(" | 0"); // FIXME: long?
		}
		if (parentheses)
			Write(')');
		return expr;
	}

	private CiBinaryExpr WriteIntegerCompare(CiBinaryExpr expr, bool parentheses, string op)
	{
		if (parentheses)
			Write('(');
		expr.Left.Accept(this, CiPriority.Rel);
		if (GetTypeCode(expr.Left.Type, false) == TypeCode.UInt32 ||
		    GetTypeCode(expr.Left.Type, false) == TypeCode.UInt64) {
			Write(" >>> 0"); // FIXME: ulong?
		} else {
			Write(" | 0"); // FIXME: long?
		}
		Write(op);
		expr.Right.Accept(this, CiPriority.Rel);
		if (GetTypeCode(expr.Right.Type, false) == TypeCode.UInt32 ||
		    GetTypeCode(expr.Right.Type, false) == TypeCode.UInt64) {
			Write(" >>> 0"); // FIXME: ulong?
		} else {
			Write(" | 0"); // FIXME: long?
		}
		if (parentheses)
			Write(')');
		return expr;
	}

	private CiBinaryExpr WriteIntegerShifRight(CiBinaryExpr expr, bool parentheses)
	{
		if (parentheses)
			Write('(');
		expr.Left.Accept(this, CiPriority.Mul);
		if (GetTypeCode(expr.Left.Type, false) == TypeCode.UInt32 ||
		    GetTypeCode(expr.Left.Type, false) == TypeCode.UInt64) {
			Write(" >>> "); // FIXME: ulong?
		} else {
			Write(" >> "); // FIXME: long?
		}
		expr.Right.Accept(this, CiPriority.Mul);
		if (parentheses)
			Write(')');
		return expr;
	}

	public override CiExpr Visit(CiBinaryExpr expr, CiPriority parent)
	{
		var leftType = GetTypeCode(expr.Left.Type, false);
		if (leftType == TypeCode.UInt32 || leftType == TypeCode.UInt64 && expr.Right.Type is CiIntegerType) {
			switch (expr.Op) {
			case CiToken.Asterisk:
				return WriteIntegerOp(expr, parent > CiPriority.Mul, " * ");
			case CiToken.Slash:
				return WriteIntegerOp(expr, parent > CiPriority.Mul, " / ");
			case CiToken.Mod:
				return WriteIntegerOp(expr, parent > CiPriority.Mul, " % ");
			case CiToken.ShiftRight:
				return WriteIntegerShifRight(expr, parent > CiPriority.Shift);
			case CiToken.Less:
				return WriteIntegerCompare(expr, parent > CiPriority.Rel, " < ");
			case CiToken.LessOrEqual:
				return WriteIntegerCompare(expr, parent > CiPriority.Rel, " <= ");
			case CiToken.Greater:
				return WriteIntegerCompare(expr, parent > CiPriority.Rel, " > ");
			case CiToken.GreaterOrEqual:
				return WriteIntegerCompare(expr, parent > CiPriority.Rel, " >= ");
			case CiToken.DivAssign:
				if (parent > CiPriority.Assign)
					Write('(');
				expr.Left.Accept(this, CiPriority.Assign);
				Write(" = ");
				WriteIntegerOp(expr, false, " / ");
				if (parent > CiPriority.Assign)
					Write('(');
				return expr;
			case CiToken.MulAssign:
				if (parent > CiPriority.Assign)
					Write('(');
				expr.Left.Accept(this, CiPriority.Assign);
				Write(" = ");
				WriteIntegerOp(expr, false, " * ");
				if (parent > CiPriority.Assign)
					Write('(');
				return expr;
			case CiToken.ModAssign:
				if (parent > CiPriority.Assign)
					Write('(');
				expr.Left.Accept(this, CiPriority.Assign);
				Write(" = ");
				WriteIntegerOp(expr, false, " % ");
				if (parent > CiPriority.Assign)
					Write('(');
				return expr;
			case CiToken.ShiftRightAssign:
				if (parent > CiPriority.Assign)
					Write('(');
				expr.Left.Accept(this, CiPriority.Assign);
				Write(" = ");
				WriteIntegerShifRight(expr, false);
				if (parent > CiPriority.Assign)
					Write('(');
				return expr;
			default:
				break;
			}
		}
		return base.Visit(expr, parent);
	}

	public override void Visit(CiAssert statement)
	{
		Write("console.assert(");
		statement.Cond.Accept(this, CiPriority.Argument);
		if (statement.Message != null) {
			Write(", ");
			statement.Message.Accept(this, CiPriority.Argument);
		}
		WriteLine(");");
	}

	public override void Visit(CiForeach statement)
	{
		Write("for (const ");
		if (statement.Count == 2) {
			Write('[');
			WriteName(statement.Element);
			Write(", ");
			WriteName(statement.ValueVar);
			WriteCall("] of Object.entries", statement.Collection);
			switch (statement.Element.Type) {
			case CiStringType _:
				if (statement.Collection.Type is CiSortedDictionaryType)
					Write(".sort((a, b) => a[0].localeCompare(b[0]))");
				break;
			case CiNumericType _:
				Write(".map(e => [+e[0], e[1]])");
				if (statement.Collection.Type is CiSortedDictionaryType)
					Write(".sort((a, b) => a[0] - b[0])");
				break;
			default:
				throw new NotImplementedException(statement.Element.Type.ToString());
			}
		}
		else {
			WriteName(statement.Element);
			Write(" of ");
			statement.Collection.Accept(this, CiPriority.Argument);
		}
		Write(')');
		WriteChild(statement.Body);
	}

	public override void Visit(CiLock statement)
	{
		throw new NotImplementedException();
	}

	public override void Visit(CiThrow statement)
	{
		Write("throw ");
		statement.Message.Accept(this, CiPriority.Argument);
		WriteLine(';');
	}

	void Write(CiEnum enu)
	{
		WriteLine();
		Write(enu.Documentation);
		Write("const ");
		Write(enu.Name);
		Write(" = ");
		OpenBlock();
		bool first = true;
		foreach (CiConst konst in enu) {
			if (!first)
				WriteLine(',');
			first = false;
			Write(konst.Documentation);
			WriteUppercaseWithUnderscores(konst.Name);
			Write(" : ");
			VisitLiteralLong(konst.Value.IntValue);
		}
		WriteLine();
		CloseBlock();
	}

	void Write(CiClass klass, CiMethod method)
	{
		if (method.CallType == CiCallType.Abstract)
			return;
		WriteLine();
		WriteDoc(method);
		Write(klass.Name);
		Write('.');
		if (method.CallType != CiCallType.Static)
			Write("prototype.");
		WriteCamelCase(method.Name);
		Write(" = function(");
		WriteParameters(method, true, true);
		WriteBody(method);
	}

	void WriteConsts(CiClass klass, IEnumerable<CiConst> consts)
	{
		foreach (CiConst konst in consts) {
			if (konst.Visibility != CiVisibility.Private || konst.Type is CiArrayStorageType) {
				WriteLine();
				Write(konst.Documentation);
				Write(klass.Name);
				Write('.');
				base.WriteVar(konst);
				WriteLine(';');
			}
		}
	}

	void Write(CiClass klass)
	{
		WriteLine();
		Write(klass.Documentation);
		Write("function ");
		Write(klass.Name);
		WriteLine("()");
		OpenBlock();
		foreach (CiField field in klass.Fields) {
			if ((field.Value != null || field.Type.IsFinal) && !field.IsAssignableStorage) {
				Write("this.");
				base.WriteVar(field);
				WriteLine(';');
				WriteInitCode(field);
			}
		}
		WriteConstructorBody(klass);
		CloseBlock();
		if (klass.BaseClassName != null) {
			Write(klass.Name);
			Write(".prototype = new ");
			Write(klass.BaseClassName);
			WriteLine("();");
		}
		WriteConsts(klass, klass.Consts);
		foreach (CiMethod method in klass.Methods)
			Write(klass, method);
		WriteConsts(klass, klass.ConstArrays);
	}

	protected void WriteLib(Dictionary<string, byte[]> resources)
	{
		WriteLine();
		Write("const Ci = ");
		OpenBlock();
		bool first = true;
		foreach (string[] method in this.Library) {
			if (method != null) {
				if (!first)
					WriteLine(',');
				first = false;
				WriteLine(method[0]);
				OpenBlock();
				for (int i = 1; i < method.Length; i++)
					WriteLine(method[i]);
				this.Indent--;
				Write('}');
			}
		}

		foreach (string name in resources.Keys.OrderBy(k => k)) {
			if (!first)
				WriteLine(',');
			first = false;
			WriteResource(name, -1);
			WriteLine(" : new Uint8Array([");
			Write('\t');
			Write(resources[name]);
			Write(" ])");
		}
		WriteLine();
		CloseBlock();
	}

	public override void Write(CiProgram program)
	{
		CreateFile(this.OutputFile);
		WriteLine();
		WriteLine("\"use strict\";");
		WriteTopLevelNatives(program);
		foreach (CiEnum enu in program.OfType<CiEnum>())
			Write(enu);
		foreach (CiClass klass in program.OfType<CiClass>()) // TODO: topological sort of class hierarchy
			Write(klass);
		if (program.Resources.Count > 0 || this.Library.Any(l => l != null))
			WriteLib(program.Resources);
		CloseFile();
	}
}

}

