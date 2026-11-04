using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.HIR;
using RobloxCS.HIR.Declarations;
using RobloxCS.AST.Types;
using Serilog;
using static RobloxCS.Lowering.Luau;
using TypeInfo = RobloxCS.AST.Types.TypeInfo;

namespace RobloxCS.Lowering;

public sealed partial class HirLowerer {
    private List<Statement> LowerTopLevelType(HirType type) {
        var symbol = type.Symbol;

        if (symbol.TypeKind != TypeKind.Class) {
            Unsupported("Top-level types which aren't classes are not supported.", type.Location);

            return [];
        }

        if (symbol.ContainingType is not null) {
            Unsupported("Nested types are not supported yet.", type.Location);

            return [];
        }

        if (symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType && !IsTranspiledType(baseType)) {
            Unsupported("Only inheritance from transpiled classes is supported.", type.Location);

            return [];
        }

        var name = ClassName(symbol);
        foreach (var member in symbol.GetMembers().Where(member => !member.IsStatic)) {
            if (member is not (IMethodSymbol { MethodKind: MethodKind.Ordinary, IsOverride: false } or IPropertySymbol { IsOverride: false })) continue;
            for (var current = symbol.BaseType; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType) {
                if (current.GetMembers(member.Name).Any(candidate => !candidate.IsStatic)) {
                    Unsupported("Hidden instance methods and properties are not supported yet.", member.Locations.FirstOrDefault() ?? type.Location);
                    return [];
                }
            }
        }
        var instanceFields = type.Fields.Where(field => !field.IsStatic).ToList();
        var staticFields = type.Fields.Where(field => field.IsStatic).ToList();

        Log.Debug(
            "Lowering {ClassKind} class {ClassName} ({InstanceFieldCount} instance fields, {StaticFieldCount} static fields, {MethodCount} methods)",
            symbol.IsStatic ? "static" : "instance", name, instanceFields.Count, staticFields.Count, type.Methods.Length
        );

        Expression classTable = Table();
        if (symbol.BaseType is { SpecialType: not SpecialType.System_Object } parent) {
            classTable = Call("setmetatable", Table(), new TableConstructorExpression {
                Fields = [new NameKey { Key = "__index", Value = Name(ReferenceType(parent, type.Location)) }],
            });
        }

        var statements = new List<Statement> { Local(name, classTable) };

        if (!symbol.IsStatic) {
            statements.Add(Assign(Index(name, "__index"), Name(name)));

            statements.Add(TypeDecl(name, LowerInstanceType(symbol, type.Location), true, symbol.TypeParameters.Select(TypeParameterName)));
        }

        foreach (var field in staticFields) {
            if (symbol.IsGenericType && !field.Symbol.IsConst) {
                Unsupported("Static state on generic classes is not supported because Luau erases type arguments.", field.Location);
                continue;
            }
            Log.Verbose("Assigning static field {ClassName}.{FieldName}", name, field.Symbol.Name);

            _staticContext = true;
            statements.AddRange(_names.Capture(() => _names.Emit(Assign(Index(name, field.Symbol.SlotName()), LowerFieldValue(field)))));
            _staticContext = false;
        }

        if (!symbol.IsStatic) {
            statements.AddRange(LowerConstructor(type, name, instanceFields));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var method in type.Methods.Where(method => !method.IsConstructor)) {
            if (method.Symbol.MethodKind is not (MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet)) {
                Unsupported($"'{method.Symbol.MethodKind}' members are not supported yet.", method.Location);

                continue;
            }

            if (!seen.Add(method.Symbol.Name)) {
                Unsupported("Method overloads are not supported yet.", method.Location);

                continue;
            }

            if (!method.Symbol.IsAbstract) statements.AddRange(LowerMethod(name, method));
        }

        return statements;
    }

    private List<Statement> LowerConstructor(HirType type, string name, List<HirField> instanceFields) {
        var constructors = type.Methods.Where(method => method.IsConstructor).ToList();
        if (constructors.Count > 1) {
            foreach (var extra in constructors.Skip(1)) {
                Unsupported("Constructor overloads are not supported yet.", extra.Location);
            }

            return [];
        }

        var constructor = constructors.FirstOrDefault();

        Log.Debug(
            "Lowering constructor {ClassName}.new ({ConstructorKind}, {ParameterCount} parameters)",
            name, constructor is { IsImplicit: false } ? "explicit" : "implicit", constructor?.Parameters.Length ?? 0
        );

        _names.BeginFunction();

        var parameters = constructor is null ? [] : LowerParameters(constructor.Parameters);
        parameters.AddRange(type.Symbol.TypeParameters.Select(parameter => (DefaultSlot(parameter), (TypeInfo)Type("any"))));
        var instanceType = LowerType(type.Symbol, type.Location);
        var genericNames = type.Symbol.TypeParameters.Select(TypeParameterName).ToList();
        var body = new List<Statement> { Local("self", Cast(Cast(NewInstance(name), Type("any")), instanceType)) };
        body.Add(Stmt(Call(Index(name, "__init"), [Name("self"), .. parameters.Select(parameter => (Expression)Name(parameter.Name))])));
        body.Add(Return(Name("self")));

        var initializerParameters = new List<(string Name, TypeInfo Type)> { ("self", instanceType) };
        initializerParameters.AddRange(parameters);
        var initializerBody = new List<Statement>();
        foreach (var parameter in type.Symbol.TypeParameters) {
            initializerBody.Add(Assign(Index("self", DefaultSlot(parameter)), Name(DefaultSlot(parameter))));
        }

        foreach (var field in instanceFields) {
            Log.Verbose(
                "Initializing field {ClassName}.{FieldName} from {Source}",
                name, field.Symbol.SlotName(), field.Initializer is null ? "its default value" : "its initializer"
            );

            initializerBody.AddRange(_names.Capture(() => _names.Emit(Assign(Index("self", field.Symbol.SlotName()), LowerFieldValue(field)))));
        }

        if (constructor?.ConstructorInitializer is { } initializer && SymbolEqualityComparer.Default.Equals(initializer.Method.ContainingType.OriginalDefinition, type.Symbol)) {
            Unsupported("Delegating constructors are not supported yet.", initializer.Location);
        } else if (type.Symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType) {
            initializerBody.AddRange(_names.Capture(() => {
                var arguments = constructor?.ConstructorInitializer?.Arguments.Select(argument => LowerExpression(argument.Value)).ToList() ?? [];
                arguments.AddRange(baseType.TypeArguments.Select(argument => LowerDefaultValue(argument, type.Location)));
                _names.Emit(Stmt(Call(Index(ReferenceType(baseType, type.Location), "__init"), [Name("self"), .. arguments])));
            }));
        }

        if (constructor is { IsImplicit: false }) {
            initializerBody.AddRange(LowerBlock(constructor.Block));
        }

        var statements = new List<Statement> { Function(name, "__init", initializerParameters, VoidType(), initializerBody, genericNames) };
        if (!type.Symbol.IsAbstract) statements.Add(Function(name, "new", parameters, instanceType, body, genericNames));
        return statements;
    }

    private List<Statement> LowerMethod(string className, HirMethod method) {
        if (method.Symbol.IsGenericMethod) {
            Unsupported("Generic methods are not supported yet.", method.Location);

            return [];
        }

        Log.Debug(
            "Lowering {MethodKind} method {ClassName}.{MethodName} ({ParameterCount} parameters)",
            method.IsStatic ? "static" : "instance", className, method.Symbol.Name, method.Parameters.Length
        );

        _names.BeginFunction();
        _staticContext = method.IsStatic;

        var parameters = LowerParameters(method.Parameters);
        if (!method.IsStatic) {
            parameters.Insert(0, ("self", LowerType(method.Symbol.ContainingType, method.Location)));
        }

        var returnType = LowerType(method.Symbol.ReturnType, method.Location);
        var body = LowerBlock(method.Block);
        _staticContext = false;

        Log.Verbose("Lowered {ClassName}.{MethodName} into {StatementCount} statements", className, method.Symbol.Name, body.Count);

        return [Function(className, method.Symbol.Name, parameters, returnType, body, method.Symbol.ContainingType.TypeParameters.Select(TypeParameterName))];
    }

    private TypeInfo LowerInstanceType(INamedTypeSymbol symbol, Location location) {
        var fields = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
        for (var current = symbol; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType) {
            foreach (var parameter in current.OriginalDefinition.TypeParameters) fields.TryAdd(DefaultSlot(parameter), Type("any"));
            foreach (var member in current.GetMembers().Where(member => !member.IsStatic)) {
                switch (member) {
                    case IFieldSymbol { AssociatedSymbol: null } field:
                        AddSlot(field.SlotName(), LowerStorageType(field, location));
                        break;
                    case IPropertySymbol property when property.IsAutoProperty():
                        AddSlot(property.Name, property.BackingField() is { } backing ? LowerStorageType(backing, location) : LowerType(property.Type, location));
                        break;
                    case IFieldSymbol { AssociatedSymbol: IPropertySymbol property } field when property.Kind() is PropertyKind.SemiAuto:
                        AddSlot(field.SlotName(), LowerStorageType(field, location));
                        break;
                    case IMethodSymbol method when method.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet:
                        if (method.AssociatedSymbol is IPropertySymbol owner && owner.IsAutoProperty()) break;
                        if (method.IsGenericMethod) break;
                        fields.TryAdd(method.Name, new CallbackTypeInfo {
                            Arguments = [TypeArgument.From("self", Type("any")), .. method.Parameters.Select(parameter => new TypeArgument { TypeInfo = LowerType(parameter.Type, location) })],
                            ReturnType = LowerType(method.ReturnType, location),
                        });
                        break;
                }
            }
        }
        return TableType(fields.Select(field => (field.Key, field.Value)));

        void AddSlot(string name, TypeInfo type) {
            if (!fields.TryAdd(name, type)) Unsupported("Hidden instance fields and properties are not supported yet.", location);
        }
    }

    private List<(string Name, TypeInfo Type)> LowerParameters(ImmutableArray<HirParameter> hirParameters) {
        var parameters = new List<(string Name, TypeInfo Type)>();

        foreach (var parameter in hirParameters) {
            if (parameter.RefKind is not RefKind.None) {
                Unsupported("ref, out and in parameters are not supported yet.", parameter.Location);
            }

            if (parameter.IsParams) {
                Unsupported("params parameters are not supported yet.", parameter.Location);
            }

            var parameterName = _names.NameFor(parameter.Symbol);
            if (parameterName != parameter.Symbol.Name) {
                Log.Verbose("Renamed parameter {CSharpName} to {LuauName}", parameter.Symbol.Name, parameterName);
            }

            parameters.Add((parameterName, LowerType(parameter.Symbol.Type, parameter.Location)));
        }

        return parameters;
    }

    private static Expression NewInstance(string name) => Call("setmetatable", Table(), Name(name));

    private Expression LowerFieldValue(HirField field) {
        if (field.Initializer is { } initializer) {
            return LowerExpression(initializer);
        }

        return LowerDefaultValue(field.Symbol.Type, field.Location);
    }

    private TypeInfo LowerStorageType(IFieldSymbol field, Location location) {
        var type = LowerType(field.Type, location);
        var startsNil = field.OriginalDefinition.Type.IsReferenceType
            && _sourceFields.TryGetValue(field.OriginalDefinition, out var source) && source.Initializer is null;
        return startsNil ? Optional(type) : type;
    }

    private static string TypeParameterName(ITypeParameterSymbol parameter) => $"__type{parameter.Ordinal}_{new string(parameter.Name.Select(character => char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_').ToArray())}";

    private static string DefaultSlot(ITypeParameterSymbol parameter) => $"__default_{parameter.ContainingType.Name}_{parameter.Ordinal}";

    private Expression LowerDefaultValue(ITypeSymbol type, Location location) {
        if (type is not ITypeParameterSymbol parameter) return DefaultValue(type);
        if (_staticContext) {
            Unsupported("default(T) in static generic-class members is not supported yet.", location);
            return Nil();
        }
        return Cast(Index("self", DefaultSlot(parameter)), LowerType(type, location));
    }

    private static Expression DefaultValue(ITypeSymbol type) {
        if (type.IsReferenceType || type.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T) {
            return Nil();
        }

        return type.SpecialType switch {
            SpecialType.System_Boolean => Bool(false),
            SpecialType.System_Char => Str("\0"),

            _ => Num(0),
        };
    }

    private TypeInfo LowerType(ITypeSymbol type, Location location) {
        if (type is ITypeParameterSymbol parameter) {
            return Type(TypeParameterName(parameter));
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) {
            return Optional(LowerType(nullable.TypeArguments[0], location));
        }

        if (type is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.Annotated }) {
            return Optional(LowerType(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), location));
        }

        switch (type.SpecialType) {
            case SpecialType.System_Void: {
                return VoidType();
            }

            case SpecialType.System_Boolean: {
                return Type("boolean");
            }

            case SpecialType.System_String:
            case SpecialType.System_Char: {
                return Type("string");
            }

            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal: {
                return Type("number");
            }
        }

        if (GetRobloxNative(type) is { } native) {
            return Type(native.Name);
        }

        if (IsTranspiledType(type)) {
            var named = (INamedTypeSymbol)type;
            return AppliedType(ReferenceType(named, location), named.TypeArguments.Select(argument => LowerType(argument, location)));
        }
        
        if (IsRobloxEnum(type)) {
            return Type($"Enum.{type.Name}");
        }

        Unsupported($"Type '{type.ToDisplayString()}' is not supported yet.", location);

        return Type("any");
    }
}