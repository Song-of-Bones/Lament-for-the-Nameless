using System;
using System.Collections.Generic;

namespace Lament.VisualNovel
{
    public sealed class NovelVariableStore
    {
        private readonly Dictionary<string, NovelValue> values = new Dictionary<string, NovelValue>(StringComparer.Ordinal);
        public event Action<string> Changed;

        public NovelVariableStore(IEnumerable<NovelVariableDefinition> definitions)
        {
            foreach (var definition in definitions) values.Add(definition.id, definition.initial.Copy());
        }

        public NovelValue Get(string id)
        {
            if (!values.TryGetValue(id, out var value)) throw new InvalidOperationException("未声明变量：" + id);
            return value.Copy();
        }

        public bool Evaluate(NovelCondition condition)
        {
            var left = Get(condition.variable);
            var right = condition.value;
            if (right == null || left.type != right.type) throw new InvalidOperationException("条件类型不匹配：" + condition.variable);
            int order;
            switch (left.type)
            {
                case NovelValueType.Boolean: order = left.boolean.CompareTo(right.boolean); break;
                case NovelValueType.Integer: order = left.integer.CompareTo(right.integer); break;
                case NovelValueType.Number: order = left.number.CompareTo(right.number); break;
                default: order = string.Compare(left.text, right.text, StringComparison.Ordinal); break;
            }
            switch (condition.comparison)
            {
                case NovelComparison.Equal: return order == 0;
                case NovelComparison.NotEqual: return order != 0;
                case NovelComparison.Greater: return order > 0;
                case NovelComparison.Less: return order < 0;
                case NovelComparison.GreaterOrEqual: return order >= 0;
                case NovelComparison.LessOrEqual: return order <= 0;
                default: throw new InvalidOperationException("未知比较运算");
            }
        }

        public void Apply(NovelOperation operation)
        {
            var old = Get(operation.variable);
            var value = operation.value;
            if (value == null || value.type != old.type) throw new InvalidOperationException("赋值类型不匹配：" + operation.variable);
            if (operation.operation == NovelOperationType.Assign) old = value.Copy();
            else
            {
                int sign = operation.operation == NovelOperationType.Add ? 1 : -1;
                if (old.type == NovelValueType.Integer) old.integer = checked(old.integer + sign * value.integer);
                else if (old.type == NovelValueType.Number) old.number += sign * value.number;
                else throw new InvalidOperationException("仅数值变量支持加减：" + operation.variable);
            }
            values[operation.variable] = old;
            Changed?.Invoke(operation.variable);
        }

        public List<NovelVariableDefinition> Export()
        {
            var result = new List<NovelVariableDefinition>();
            foreach (var pair in values) result.Add(new NovelVariableDefinition { id = pair.Key, initial = pair.Value.Copy() });
            return result;
        }

        public void Restore(IEnumerable<NovelVariableDefinition> saved)
        {
            foreach (var item in saved) Apply(new NovelOperation { variable = item.id, value = item.initial });
        }
    }
}
