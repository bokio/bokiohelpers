using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Bokio.Tests.Verifiers
{
    public class CopyValuesFromAsserter<T>
    {
        protected Dictionary<string, PropertySetup> properties;

        public CopyValuesFromAsserter()
        {
            properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => new PropertySetup
            {
                Info = x,
                Name = x.Name,
                ShouldCopy = true
            }).ToDictionary(x => x.Name);
        }

        public virtual CopyValuesFromAsserter<T> ShouldNotCopy<TProp>(Expression<Func<T, TProp>> prop, TProp expected = default(TProp), bool assertExpected = false)
        {
            string name = GetPropertyName(prop);

            properties[name].ShouldCopy = false;
            properties[name].Expected = expected;
            properties[name].AssertExpected = assertExpected;

            return this;
        }

        public virtual CopyValuesFromAsserter<T> AssertWith<TProp>(Expression<Func<T, TProp>> prop, CopyValuesFromAsserter<TProp> asserter) where TProp : class
        {
            string name = GetPropertyName(prop);

            properties[name].ShouldCopy = true;
            properties[name].Asserter = (original, copy) => asserter.Assert((TProp)original, (TProp)copy);

            return this;
        }

        public virtual CopyValuesFromAsserter<T> AssertIEnumerableWith<TProp>(Expression<Func<T, IEnumerable<TProp>>> prop, CopyValuesFromAsserter<TProp> asserter) where TProp : class
        {
            string name = GetPropertyName(prop);

            properties[name].ShouldCopy = true;
            properties[name].IsEnumerable = true;
            properties[name].Asserter = (original, copy) =>
            {
                var oList = (original as IEnumerable<TProp>);
                var cList = (copy as IEnumerable<TProp>);

                cList.Count().ShouldBe(oList.Count());
                var zipped = oList.Zip(cList, (o, c) => new { o, c });
                foreach (var item in zipped)
                {
                    asserter.Assert(item.o, item.c);
                }

            };

            return this;
        }

        public virtual CopyValuesFromAsserter<T> AssertIEnumerableWith<TKey, TProp>(Expression<Func<T, IEnumerable<KeyValuePair<TKey, TProp>>>> prop, CopyAsserter<TProp> asserter) where TProp : class
        {
            string name = GetPropertyName(prop);

            properties[name].ShouldCopy = true;
            properties[name].IsEnumerable = true;
            properties[name].Asserter = (original, copy) =>
            {
                var oList = (original as IEnumerable<KeyValuePair<TKey, TProp>>);
                var cList = (copy as IEnumerable<KeyValuePair<TKey, TProp>>);

                cList.Count().ShouldBe(oList.Count());
                var zipped = oList.Zip(cList, (o, c) => new { o, c });
                foreach (var item in zipped)
                {
                    asserter.Assert(item.o.Value, item.c.Value);
                }

            };

            return this;
        }

        protected static string GetPropertyName<TProp>(Expression<Func<T, TProp>> prop)
        {
            MemberExpression body = prop.Body as MemberExpression;

            if (body == null)
            {
                UnaryExpression ubody = (UnaryExpression)prop.Body;
                body = ubody.Operand as MemberExpression;
            }

            var name = body.Member.Name;
            return name;
        }

        public virtual void Assert(T original, T copy)
        {
            if (original == null && copy == null)
            {
                return;
            }
            foreach (var item in properties.Values)
            {
                var copyValue = item.Info.GetValue(copy);
                if (item.ShouldCopy)
                {
                    var originalValue = item.Info.GetValue(original);

                    var type = item.Info.PropertyType;
                    if (type.IsValueType)
                    {
                        if (
                            (Nullable.GetUnderlyingType(type) == null && Activator.CreateInstance(type).Equals(copyValue))
                            || (Nullable.GetUnderlyingType(type) != null && copyValue == null)
                        )
                        {
                            throw new Exception($"{typeof(T).Name}:{item.Name} had a default value and was not ignored");
                        }
                    }

                    if (IsSimpleType(item.Info.PropertyType))
                    {
                        copyValue.ShouldBe(originalValue, $"{item.Name} was not copied");
                    }
                    else
                    {
                        item.Asserter.ShouldNotBeNull($"{item.Name}: Missing asserter");

                        item.Asserter(originalValue, copyValue);
                    }
                }
                else if (item.AssertExpected)
                {
                    copyValue.ShouldBe(item.Expected, $"{item.Name} was not equal to expected value {item.Expected}");
                }
            }
        }

        protected static bool IsSimpleType(Type type)
        {
            return
                type.IsPrimitive ||
                type.IsEnum ||
                new Type[] {
                    typeof(String),
                    typeof(Decimal),
                    typeof(DateTime),
                    typeof(DateTimeOffset),
                    typeof(TimeSpan),
                    typeof(Guid)
                }.Contains(type) ||
                Convert.GetTypeCode(type) != TypeCode.Object ||
                (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) && IsSimpleType(type.GetGenericArguments()[0]))
                ;
        }

        protected class PropertySetup
        {
            public string Name { get; set; }
            public PropertyInfo Info { get; set; }
            public bool ShouldCopy { get; set; }
            public object Expected { get; internal set; }
            public bool AssertExpected { get; internal set; }
            /// <summary>
            /// Action (original, copy)
            /// </summary>
            public Action<object, object> Asserter { get; set; }
            public bool IsEnumerable { get; internal set; }
        }

        protected static string GetName(Expression<Func<object>> exp)
        {
            MemberExpression body = exp.Body as MemberExpression;

            if (body == null)
            {
                UnaryExpression ubody = (UnaryExpression)exp.Body;
                body = ubody.Operand as MemberExpression;
            }

            return body.Member.Name;
        }
    }

    public class IEnumerableCopyValuesFromAsserter<T> : CopyValuesFromAsserter<IEnumerable<T>>
    {
        public override void Assert(IEnumerable<T> original, IEnumerable<T> copy)
        {
            original.Count().ShouldBe(copy.Count());

            var zipped = original.Zip(copy, (originalItem, copyItem) => new { originalItem, copyItem });
            var index = 0;
            foreach (var zipItem in zipped)
            {
                foreach (var item in properties.Values)
                {
                    var copyValue = item.Info.GetValue(zipItem.copyItem);
                    if (item.ShouldCopy)
                    {
                        var originalValue = item.Info.GetValue(zipItem.originalItem);
                        var type = item.Info.PropertyType;
                        if (type.IsValueType && Activator.CreateInstance(type).Equals(copyValue))
                        {
                            throw new Exception($"{item.Name} had a default value and was not ignored");
                        }

                        if (IsSimpleType(item.Info.PropertyType))
                        {
                            copyValue.ShouldBe(originalValue, $"{item.Name} at index {index} was not copied");
                        }
                        else
                        {
                            item.Asserter.ShouldNotBeNull($"{item.Name}: at index {index} Missing asserter");

                            item.Asserter(originalValue, copyValue);
                        }
                    }
                    else if (item.AssertExpected)
                    {
                        copyValue.ShouldBe(item.Expected, $"{item.Name} at index {index} was not equal to expected value {item.Expected}");
                    }
                }

                index++;
            }
        }
    }
}
