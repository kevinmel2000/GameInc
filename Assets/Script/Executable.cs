﻿using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Script {

[Serializable]
public class TypedExecutable<T> {
    [SerializeField] private readonly Executable executable;

    private TypedExecutable(Executable executable) {
        this.executable = executable;
    }

    public static TypedExecutable<T> FromScript(string script,
        ParserContext parserContext) {
        if (typeof(T) == typeof(Void)) {
            Debug.LogError("TypedExecutable<T = Void> : Void type not allowed.");
            return null;
        }
        Executable executable = Executable.FromScript(script, parserContext);
        if (executable == null) {
            Debug.LogError($"TypedExecutable<T = {typeof(T)}>.FromScript : " +
                           $"parsing error in  :\n{script}");
            return null;
        }
        if (!Executable.TypeCompatibility<T>(executable.Type, executable.ArrayType)) {
            Debug.LogError($"TypedExecutable<T = {typeof(T)}>.FromScript : T " +
                           $"is not compatible with {executable.Type}.");
            return null;
        }
        return new TypedExecutable<T>(executable);
    }

    public bool Compute(IScriptContext context, out T result) {
        ISymbol lastResult = executable.Execute(context);
        if (lastResult == null) {
            Debug.LogError($"TypedExecutable<T = {typeof(T)}>.Compute : execution error.");
            result = default(T);
            return false;
        }
        Assert.IsTrue(lastResult.Type() == executable.Type);
        Symbol<T> lastResultTyped = lastResult as Symbol<T>;
        Assert.IsNotNull(lastResultTyped);
        result = lastResultTyped.Value;
        return true;
    }
}

/// <summary>
/// The Executable class allows to easily parse and evaluate a
/// sequence of type-checked instructions.
///
/// Single-line comments ('//') are supported.
/// </summary>
[Serializable]
public class Executable {
    [SerializeField] private SymbolType type;
    public SymbolType Type => type;

    [SerializeField] private SymbolType arrayType;
    public SymbolType ArrayType => arrayType;

    [SerializeField] private List<IExpression> expressions;

    private Executable(SymbolType type, List<IExpression> expressions) {
        this.type = type;
        this.expressions = expressions;
    }

    /// <summary>
    /// Try to parse the given text script as an Executable sequence of Expressions.
    ///
    /// The return Type is determined by the last Expression : if it ends with ';',
    /// then it is considered as evaluating to Void, otherwise the Type will be
    /// inferred from the script of that Expression.
    /// </summary>
    public static Executable FromScript(string script,
        ParserContext parserContext) {
        // Expressions split
        bool inStringLiteral = false;
        string currentExpression = "";
        List<string> expressionsString = new List<string>();
        for (int i = 0; i < script.Length; i++) {
            char c = script[i];
            if (c == '/' && i + 1 < script.Length && script[i + 1] == '/') { // single-line comments
                i = i + 2;
                for (int j = i; j < script.Length; j++) {
                    if (script[j] == '\n') {
                        i = j;
                        break;
                    }
                    if (j + 1 < script.Length &&
                        script[j] == '\r' && script[j + 1] == '\n') { // C# verbatim strings use "\r\n"
                        i = j + 1;
                        break;
                    }
                }
                currentExpression = currentExpression.Trim();
                expressionsString.Add(currentExpression.Trim());
                currentExpression = "";
                continue;
            }
            //Debug.LogWarning(currentExpression);
            currentExpression += c;
            if (c == '\'') {
                if (!inStringLiteral) inStringLiteral = true;
                else if (i > 0 && script[i - 1] != '\\') inStringLiteral = false;
            } else if (c == ';' && !inStringLiteral) {
                currentExpression = currentExpression.TrimStart();
                if (currentExpression != ";")
                    expressionsString.Add(currentExpression.TrimStart());
                currentExpression = "";
            }
        }
        currentExpression = currentExpression.Trim();
        if (currentExpression != "") expressionsString.Add(currentExpression);

        // Expressions parsing
        SymbolType returnType, returnArrayType;
        List<IExpression> expressions = ParseExpressionSequence(
            expressionsString.ToArray(), parserContext, out returnType,
            out returnArrayType);
        if (expressions == null) {
            Debug.LogError("Executable.FromScript(...) : parsing error.");
            return null;
        }
        if (returnType == SymbolType.Invalid) {
            Debug.LogError( "Executable.FromScript(...) : could not determing Type " +
                           $"from last expression \"{expressions.Last().Script()}\".");
            return null;
        }

        return new Executable(returnType, expressions);
    }

    private static List<IExpression> ParseExpressionSequence(
        string[] expressionsString, ParserContext context,
        out SymbolType returnType, out SymbolType returnArrayType) {
        returnType = SymbolType.Invalid;
        returnArrayType = SymbolType.Invalid;
        List<IExpression> expressions = new List<IExpression>();
        if (expressionsString.Length == 0) {
            returnType = SymbolType.Void;
            expressions.Add(new SymbolExpression<Void>(new VoidSymbol()));
            Debug.LogWarning("Executable.ParseExpressionSequence(...) : empty sequence.");
            return expressions;
        }
        for (int i = 0; i < expressionsString.Length; i++) {
            string expressionString = expressionsString[i];
            if (expressionString == "") continue;
            IExpression expression = Parser.ParseExpression(expressionString, context);
            if (expression == null) {
                Debug.LogError( "Executable.ParseExpressionSequence(...) : parsing error at " +
                                $"line {i+1} \"{expressionString}\".");
                return null;
            }
            if (i == expressionsString.Length - 1) {
                // last expression determines return Type
                returnArrayType = expression.ArrayType();
                returnType = expressionString.EndsWith(";") ?
                    SymbolType.Void : expression.Type();
            }
            expressions.Add(expression);
        }
        return expressions;
    }

    public ISymbol Execute(IScriptContext context) {
        ISymbol result = new VoidSymbol();
        for (int i = 0; i < expressions.Count; i++) {
            IExpression expression = expressions[i];
            result = expression.EvaluateAsISymbol(context);
            if (result == null) {
                Debug.LogError( "Executable : error while evaluating expression " +
                               $"n°{i+1} \"{expression.Script()}\".");
                return null;
            }
        }
        return result;
    }

    public bool ExecuteExpecting<T>(IScriptContext context, out T result)
        where T : struct {
        result = default(T);
        if (type == SymbolType.Void) {
            Debug.LogError($"Executable.ExecuteExpecting : cannot expect {typeof(T)}. " +
                           "Use Execute() or ExecuteExpectingArray() instead.");
            return false;
        }
        ISymbol lastResult = Execute(context);
        if (lastResult == null || lastResult.Type() != type) {
            Debug.LogError("Executable.ExecuteExpecting : execution error.");
            return false;
        }
        if (!TypeCompatibility<T>(lastResult.Type(), lastResult.ArrayType())) {
            Debug.LogError($"Executable.ExecuteExpecting : result type {typeof(T)} " +
                           $"imcompatible with executable type {type}.");
            return false;
        }
        Symbol<T> lastResultTyped = lastResult as Symbol<T>;
        Assert.IsNotNull(lastResultTyped);
        result = lastResultTyped.Value;
        return true;
    }

    public bool ExecuteExpectingArray<T>(IScriptContext context, out T result)
        where T: class {
        result = default(T);
        if (type != SymbolType.Array) {
            Debug.LogError($"Executable.ExecuteExpectingArray : cannot expect {typeof(T)}. " +
                            "Use Execute() or ExecuteExpecting() instead.");
            return false;
        }
        ISymbol lastResult = Execute(context);
        if (lastResult == null || lastResult.Type() != type) {
            Debug.LogError("Executable.ExecuteExpectingArray : execution error.");
            return false;
        }
        if (!TypeCompatibility<T>(lastResult.Type(), lastResult.ArrayType())) {
            Debug.LogError($"Executable.ExecuteExpecting : result type {typeof(T)} " +
                           $"imcompatible with executable type {type}.");
            return false;
        }
        switch (lastResult.ArrayType()) {
            case SymbolType.Void:
                result = ((ArraySymbol<Void>) lastResult).Value.Elements.Select(
                    e => e.Evaluate(context).Value).ToArray() as T;
                break;
            case SymbolType.Boolean:
                result = ((ArraySymbol<bool>) lastResult).Value.Elements.Select(
                    e => e.Evaluate(context).Value).ToArray() as T;
                break;
            case SymbolType.Integer:
                result = ((ArraySymbol<int>) lastResult).Value.Elements.Select(
                    e => e.Evaluate(context).Value).ToArray() as T;
                break;
            case SymbolType.Float:
                result = ((ArraySymbol<float>) lastResult).Value.Elements.Select(
                    e => e.Evaluate(context).Value).ToArray() as T;
                break;
            case SymbolType.Id:
                result = ((ArraySymbol<Id>) lastResult).Value.Elements.Select(
                    e => e.Evaluate(context).Value.Identifier).ToArray() as T;
                break;
            case SymbolType.String:
                result = ((ArraySymbol<string>) lastResult).Value.Elements.Select(
                    e => e.Evaluate(context).Value).ToArray() as T;
                break;
            case SymbolType.Date:
                result = ((ArraySymbol<DateTime>) lastResult).Value.Elements.Select(
                    e => e.Evaluate(context).Value).ToArray() as T;
                break;
            default: throw new ArgumentOutOfRangeException();
        }
        return true;
    }

    public static bool TypeCompatibility<T>(SymbolType type, SymbolType arrayType) {
        // TODO : differenciate ID and String
        if (type == SymbolType.Array)
            return typeof(T) == typeof(bool[]) && arrayType == SymbolType.Boolean ||
               typeof(T) == typeof(int[]) && arrayType == SymbolType.Integer ||
               typeof(T) == typeof(float[]) && arrayType == SymbolType.Float ||
               typeof(T) == typeof(Id[]) && arrayType == SymbolType.Id ||
               typeof(T) == typeof(string[]) && arrayType == SymbolType.String ||
               typeof(T) == typeof(DateTime[]) && arrayType == SymbolType.Date;
        return typeof(T) == typeof(bool) && type == SymbolType.Boolean ||
            typeof(T) == typeof(int) && type == SymbolType.Integer ||
            typeof(T) == typeof(float) && type == SymbolType.Float ||
            typeof(T) == typeof(Id) && type == SymbolType.Id ||
            typeof(T) == typeof(string) && type == SymbolType.String ||
            typeof(T) == typeof(DateTime) && type == SymbolType.Date;
    }
}

}
