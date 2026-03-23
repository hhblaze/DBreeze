/*
 Made by Frantisek Konopecky, Prague, 2014 - 2016
 Fast Deep Copy by Expression Trees https://www.codeproject.com/Articles/1111658/Fast-Deep-Copy-by-Expression-Trees-C-Sharp?fid=1907758&select=5469139&fr=1&tid=5467411
 Code comes under MIT licence - Can be used without  limitations for both personal and commercial purposes.

 Adopted for .NET8_Or_Greater by DBreeze team.
 */

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DBreeze.Utils
{
    /// <summary>
    /// return (new MyObject()).CloneByExpressionTree();
    /// </summary>
    public static class DeepCopyByExpressionTrees
    {
        // Replaced locking/dictionary swapping with highly-optimized ConcurrentDictionary
        private static readonly ConcurrentDictionary<Type, bool> IsStructTypeToDeepCopyDictionary = new();

        private static readonly ConcurrentDictionary<Type, Func<object, Dictionary<object, object>, object>> CompiledCopyFunctionsDictionary = new();

        private static readonly Type ObjectType = typeof(object);
        private static readonly Type ObjectDictionaryType = typeof(Dictionary<object, object>);

        /// <summary>
        /// Creates a deep copy of an object.
        /// </summary>
        /// <typeparam name="T">Object type.</typeparam>
        /// <param name="original">Object to copy.</param>
        /// <param name="copiedReferencesDict">Dictionary of already copied objects (Keys: original objects, Values: their copies).</param>
        /// <returns></returns>
        public static T DeepCopyByExpressionTree<T>(this T original, Dictionary<object, object>? copiedReferencesDict = null)
        {
            return (T)DeepCopyByExpressionTreeObj(original, false, copiedReferencesDict ?? new Dictionary<object, object>(ReferenceEqualityComparer.Instance))!;
        }

        /// <summary>
        /// Creates a deep copy of an object (Works 3 times faster than ProtobufClone).
        /// </summary>
        public static T CloneByExpressionTree<T>(this T original, Dictionary<object, object>? copiedReferencesDict = null)
        {
            return (T)DeepCopyByExpressionTreeObj(original, false, copiedReferencesDict ?? new Dictionary<object, object>(ReferenceEqualityComparer.Instance))!;
        }

        private static object? DeepCopyByExpressionTreeObj(object? original, bool forceDeepCopy, Dictionary<object, object> copiedReferencesDict)
        {
            if (original is null)
            {
                return null;
            }

            var type = original.GetType();

            if (IsDelegate(type))
            {
                return null;
            }

            if (!forceDeepCopy && !IsTypeToDeepCopy(type))
            {
                return original;
            }

            // Inline out var
            if (copiedReferencesDict.TryGetValue(original, out var alreadyCopiedObject))
            {
                return alreadyCopiedObject;
            }

            if (type == ObjectType)
            {
                return new object();
            }

            var compiledCopyFunction = GetOrCreateCompiledLambdaCopyFunction(type);
            return compiledCopyFunction(original, copiedReferencesDict);
        }

        private static Func<object, Dictionary<object, object>, object> GetOrCreateCompiledLambdaCopyFunction(Type type)
        {
            // Replaced entire lock and manual copy implementation with GetOrAdd
            return CompiledCopyFunctionsDictionary.GetOrAdd(type, t =>
                CreateCompiledLambdaCopyFunctionForType(t).Compile());
        }

        private static Expression<Func<object, Dictionary<object, object>, object>> CreateCompiledLambdaCopyFunctionForType(Type type)
        {
            // Inline out declarations via the refactored Initialization method
            InitializeExpressions(type,
                                  out var inputParameter,
                                  out var inputDictionary,
                                  out var outputVariable,
                                  out var boxingVariable,
                                  out var endLabel,
                                  out var variables,
                                  out var expressions);

            IfNullThenReturnNullExpression(inputParameter, endLabel, expressions);
            MemberwiseCloneInputToOutputExpression(type, inputParameter, outputVariable, expressions);

            if (IsClassOtherThanString(type))
            {
                StoreReferencesIntoDictionaryExpression(inputParameter, inputDictionary, outputVariable, expressions);
            }

            FieldsCopyExpressions(type, inputParameter, inputDictionary, outputVariable, boxingVariable, expressions);

            if (IsArray(type) && IsTypeToDeepCopy(type.GetElementType()!))
            {
                CreateArrayCopyLoopExpression(type, inputParameter, inputDictionary, outputVariable, variables, expressions);
            }

            return CombineAllIntoLambdaFunctionExpression(inputParameter, inputDictionary, outputVariable, endLabel, variables, expressions);
        }

        private static void InitializeExpressions(Type type,
                                                  out ParameterExpression inputParameter,
                                                  out ParameterExpression inputDictionary,
                                                  out ParameterExpression outputVariable,
                                                  out ParameterExpression boxingVariable,
                                                  out LabelTarget endLabel,
                                                  out List<ParameterExpression> variables,
                                                  out List<Expression> expressions)
        {
            inputParameter = Expression.Parameter(ObjectType);
            inputDictionary = Expression.Parameter(ObjectDictionaryType);
            outputVariable = Expression.Variable(type);
            boxingVariable = Expression.Variable(ObjectType);
            endLabel = Expression.Label();

            // Target-typed new and Collection Expressions
            variables = [outputVariable, boxingVariable];
            expressions = [];
        }

        private static void IfNullThenReturnNullExpression(ParameterExpression inputParameter, LabelTarget endLabel, List<Expression> expressions)
        {
            var ifNullThenReturnNullExpression =
                Expression.IfThen(
                    Expression.Equal(inputParameter, Expression.Constant(null, ObjectType)),
                    Expression.Return(endLabel));

            expressions.Add(ifNullThenReturnNullExpression);
        }

        private static void MemberwiseCloneInputToOutputExpression(
            Type type, ParameterExpression inputParameter, ParameterExpression outputVariable, List<Expression> expressions)
        {
            var memberwiseCloneMethod = ObjectType.GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var memberwiseCloneInputExpression =
                Expression.Assign(
                    outputVariable,
                    Expression.Convert(Expression.Call(inputParameter, memberwiseCloneMethod), type));

            expressions.Add(memberwiseCloneInputExpression);
        }

        private static void StoreReferencesIntoDictionaryExpression(ParameterExpression inputParameter, ParameterExpression inputDictionary, ParameterExpression outputVariable, List<Expression> expressions)
        {
            var storeReferencesExpression =
                Expression.Assign(
                    Expression.Property(inputDictionary, ObjectDictionaryType.GetProperty("Item")!, inputParameter),
                    Expression.Convert(outputVariable, ObjectType));

            expressions.Add(storeReferencesExpression);
        }

        private static Expression<Func<object, Dictionary<object, object>, object>> CombineAllIntoLambdaFunctionExpression(
            ParameterExpression inputParameter, ParameterExpression inputDictionary, ParameterExpression outputVariable, LabelTarget endLabel, List<ParameterExpression> variables, List<Expression> expressions)
        {
            expressions.Add(Expression.Label(endLabel));
            expressions.Add(Expression.Convert(outputVariable, ObjectType));

            var finalBody = Expression.Block(variables, expressions);
            return Expression.Lambda<Func<object, Dictionary<object, object>, object>>(finalBody, inputParameter, inputDictionary);
        }

        private static void CreateArrayCopyLoopExpression(Type type, ParameterExpression inputParameter, ParameterExpression inputDictionary, ParameterExpression outputVariable, List<ParameterExpression> variables, List<Expression> expressions)
        {
            var rank = type.GetArrayRank();
            var indices = GenerateIndices(rank);
            variables.AddRange(indices);

            var elementType = type.GetElementType()!;
            var assignExpression = ArrayFieldToArrayFieldAssignExpression(inputParameter, inputDictionary, outputVariable, elementType, type, indices);

            Expression forExpression = assignExpression;

            for (int dimension = 0; dimension < rank; dimension++)
            {
                var indexVariable = indices[dimension];
                forExpression = LoopIntoLoopExpression(inputParameter, indexVariable, forExpression, dimension);
            }

            expressions.Add(forExpression);
        }

        private static List<ParameterExpression> GenerateIndices(int arrayRank)
        {
            var indices = new List<ParameterExpression>(arrayRank);
            for (int i = 0; i < arrayRank; i++)
            {
                indices.Add(Expression.Variable(typeof(int)));
            }
            return indices;
        }

        private static BinaryExpression ArrayFieldToArrayFieldAssignExpression(
            ParameterExpression inputParameter, ParameterExpression inputDictionary, ParameterExpression outputVariable, Type elementType, Type arrayType, List<ParameterExpression> indices)
        {
            var indexTo = Expression.ArrayAccess(outputVariable, indices);
            var indexFrom = Expression.ArrayIndex(Expression.Convert(inputParameter, arrayType), indices);
            var forceDeepCopy = elementType != ObjectType;

            var rightSide =
                Expression.Convert(
                    Expression.Call(
                        DeepCopyByExpressionTreeObjMethod,
                        Expression.Convert(indexFrom, ObjectType),
                        Expression.Constant(forceDeepCopy, typeof(bool)),
                        inputDictionary),
                    elementType);

            return Expression.Assign(indexTo, rightSide);
        }

        private static BlockExpression LoopIntoLoopExpression(
            ParameterExpression inputParameter, ParameterExpression indexVariable, Expression loopToEncapsulate, int dimension)
        {
            var lengthVariable = Expression.Variable(typeof(int));
            var endLabelForThisLoop = Expression.Label();

            var newLoop =
                Expression.Loop(
                    Expression.Block(
                        [], // C# 12 empty array collection expression
                        Expression.IfThen(
                            Expression.GreaterThanOrEqual(indexVariable, lengthVariable),
                            Expression.Break(endLabelForThisLoop)),
                        loopToEncapsulate,
                        Expression.PostIncrementAssign(indexVariable)),
                    endLabelForThisLoop);

            var lengthAssignment = GetLengthForDimensionExpression(lengthVariable, inputParameter, dimension);
            var indexAssignment = Expression.Assign(indexVariable, Expression.Constant(0));

            return Expression.Block([lengthVariable], lengthAssignment, indexAssignment, newLoop);
        }

        private static BinaryExpression GetLengthForDimensionExpression(ParameterExpression lengthVariable, ParameterExpression inputParameter, int i)
        {
            var getLengthMethod = typeof(Array).GetMethod("GetLength", BindingFlags.Public | BindingFlags.Instance)!;
            var dimensionConstant = Expression.Constant(i);

            return Expression.Assign(
                lengthVariable,
                Expression.Call(
                    Expression.Convert(inputParameter, typeof(Array)),
                    getLengthMethod,
                    [dimensionConstant])); // C# 12 Collection expression
        }

        private static void FieldsCopyExpressions(Type type, ParameterExpression inputParameter, ParameterExpression inputDictionary, ParameterExpression outputVariable, ParameterExpression boxingVariable, List<Expression> expressions)
        {
            var fields = GetAllRelevantFields(type);

            var readonlyFields = fields.Where(f => f.IsInitOnly).ToList();
            var writableFields = fields.Where(f => !f.IsInitOnly).ToList();

            bool shouldUseBoxing = readonlyFields.Count > 0;

            if (shouldUseBoxing)
            {
                var boxingExpression = Expression.Assign(boxingVariable, Expression.Convert(outputVariable, ObjectType));
                expressions.Add(boxingExpression);
            }

            foreach (var field in readonlyFields)
            {
                if (IsDelegate(field.FieldType))
                {
                    ReadonlyFieldToNullExpression(field, boxingVariable, expressions);
                }
                else
                {
                    ReadonlyFieldCopyExpression(type, field, inputParameter, inputDictionary, boxingVariable, expressions);
                }
            }

            if (shouldUseBoxing)
            {
                var unboxingExpression = Expression.Assign(outputVariable, Expression.Convert(boxingVariable, type));
                expressions.Add(unboxingExpression);
            }

            foreach (var field in writableFields)
            {
                if (IsDelegate(field.FieldType))
                {
                    WritableFieldToNullExpression(field, outputVariable, expressions);
                }
                else
                {
                    WritableFieldCopyExpression(type, field, inputParameter, inputDictionary, outputVariable, expressions);
                }
            }
        }

        private static FieldInfo[] GetAllRelevantFields(Type type, bool forceAllFields = false)
        {
            var fieldsList = new List<FieldInfo>();
            var typeCache = type;

            while (typeCache != null)
            {
                fieldsList.AddRange(
                    typeCache
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                        .Where(field => forceAllFields || IsTypeToDeepCopy(field.FieldType)));

                typeCache = typeCache.BaseType;
            }

            return [.. fieldsList]; // C# 12 Collection expressions spread operator
        }

        private static FieldInfo[] GetAllFields(Type type) => GetAllRelevantFields(type, forceAllFields: true);

        private static readonly Type FieldInfoType = typeof(FieldInfo);
        private static readonly MethodInfo SetValueMethod = FieldInfoType.GetMethod("SetValue", [ObjectType, ObjectType])!;

        private static void ReadonlyFieldToNullExpression(FieldInfo field, ParameterExpression boxingVariable, List<Expression> expressions)
        {
            var fieldToNullExpression = Expression.Call(
                Expression.Constant(field),
                SetValueMethod,
                boxingVariable,
                Expression.Constant(null, field.FieldType));

            expressions.Add(fieldToNullExpression);
        }

        private static readonly Type ThisType = typeof(DeepCopyByExpressionTrees);
        private static readonly MethodInfo DeepCopyByExpressionTreeObjMethod = ThisType.GetMethod("DeepCopyByExpressionTreeObj", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static void ReadonlyFieldCopyExpression(Type type, FieldInfo field, ParameterExpression inputParameter, ParameterExpression inputDictionary, ParameterExpression boxingVariable, List<Expression> expressions)
        {
            var fieldFrom = Expression.Field(Expression.Convert(inputParameter, type), field);
            var forceDeepCopy = field.FieldType != ObjectType;

            var fieldDeepCopyExpression =
                Expression.Call(
                    Expression.Constant(field, FieldInfoType),
                    SetValueMethod,
                    boxingVariable,
                    Expression.Call(
                        DeepCopyByExpressionTreeObjMethod,
                        Expression.Convert(fieldFrom, ObjectType),
                        Expression.Constant(forceDeepCopy, typeof(bool)),
                        inputDictionary));

            expressions.Add(fieldDeepCopyExpression);
        }

        private static void WritableFieldToNullExpression(FieldInfo field, ParameterExpression outputVariable, List<Expression> expressions)
        {
            var fieldTo = Expression.Field(outputVariable, field);
            var fieldToNullExpression = Expression.Assign(fieldTo, Expression.Constant(null, field.FieldType));
            expressions.Add(fieldToNullExpression);
        }

        private static void WritableFieldCopyExpression(Type type, FieldInfo field, ParameterExpression inputParameter, ParameterExpression inputDictionary, ParameterExpression outputVariable, List<Expression> expressions)
        {
            var fieldFrom = Expression.Field(Expression.Convert(inputParameter, type), field);
            var fieldTo = Expression.Field(outputVariable, field);
            var forceDeepCopy = field.FieldType != ObjectType;

            var fieldDeepCopyExpression =
                Expression.Assign(
                    fieldTo,
                    Expression.Convert(
                        Expression.Call(
                            DeepCopyByExpressionTreeObjMethod,
                            Expression.Convert(fieldFrom, ObjectType),
                            Expression.Constant(forceDeepCopy, typeof(bool)),
                            inputDictionary),
                        field.FieldType));

            expressions.Add(fieldDeepCopyExpression);
        }

        private static bool IsArray(Type type) => type.IsArray;

        private static bool IsDelegate(Type type) => typeof(Delegate).IsAssignableFrom(type);

        private static bool IsTypeToDeepCopy(Type type) => IsClassOtherThanString(type) || IsStructWhichNeedsDeepCopy(type);

        private static bool IsClassOtherThanString(Type type) => !type.IsValueType && type != typeof(string);

        private static bool IsStructWhichNeedsDeepCopy(Type type)
        {
            // Replaced locking/dictionary logic with GetOrAdd
            return IsStructTypeToDeepCopyDictionary.GetOrAdd(type, IsStructWhichNeedsDeepCopy_NoDictionaryUsed);
        }

        private static bool IsStructWhichNeedsDeepCopy_NoDictionaryUsed(Type type)
        {
            return IsStructOtherThanBasicValueTypes(type) && HasInItsHierarchyFieldsWithClasses(type);
        }

        private static bool IsStructOtherThanBasicValueTypes(Type type)
        {
            return type.IsValueType
                   && !type.IsPrimitive
                   && !type.IsEnum
                   && type != typeof(decimal);
        }

        private static bool HasInItsHierarchyFieldsWithClasses(Type type, HashSet<Type>? alreadyCheckedTypes = null)
        {
            alreadyCheckedTypes ??= []; // Target typed new collection initialization
            alreadyCheckedTypes.Add(type);

            var allFields = GetAllFields(type);
            var allFieldTypes = allFields.Select(f => f.FieldType).Distinct().ToList();

            if (allFieldTypes.Exists(IsClassOtherThanString))
            {
                return true;
            }

            var notBasicStructsTypes = allFieldTypes.Where(IsStructOtherThanBasicValueTypes).ToList();
            var typesToCheck = notBasicStructsTypes.Where(t => !alreadyCheckedTypes.Contains(t)).ToList();

            foreach (var typeToCheck in typesToCheck)
            {
                if (HasInItsHierarchyFieldsWithClasses(typeToCheck, alreadyCheckedTypes))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
