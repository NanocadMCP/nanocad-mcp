using System.Globalization;

namespace NanoCAD.Contracts;

// Arithmetic expressions only, never a command/script evaluator. Variable names
// are resolved explicitly against the selected body's current MCS inventory.
public sealed class ModelArithmetic
{
    private abstract record Node;
    private sealed record Constant(double Value):Node;
    private sealed record Reference(string Name):Node;
    private sealed record Unary(char Operator,Node Value):Node;
    private sealed record Binary(char Operator,Node Left,Node Right):Node;
    private const int StoredByteBudget=8*1024*1024;
    private static readonly System.Text.UTF8Encoding Utf8=new(false,true);
    private readonly Node _root;private readonly Action? _cancel;
    private ModelArithmetic(Node root,Action? cancel)
    {
        _root=root;_cancel=cancel;
        var names=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);var pending=new Stack<Node>();pending.Push(root);
        while(pending.Count>0)
        {
            cancel?.Invoke();var node=pending.Pop();
            if(node is Reference reference){if(seen.Add(reference.Name))names.Add(reference.Name);}
            else if(node is Unary unary)pending.Push(unary.Value);
            else if(node is Binary binary){pending.Push(binary.Right);pending.Push(binary.Left);}
        }
        References=names.ToArray();Canonical=Render(root,cancel);
    }
    public string[] References {get;}
    public string Canonical {get;}
    // Canonical normalizes expressions; identifier validation needs the AST
    // kind and the exact original spelling, including absence of wrappers.
    public bool IsExactIdentifier(string text)=>_root is Reference reference&&string.Equals(reference.Name,text,StringComparison.Ordinal);
    public string RenameReference(string before,string after)
    {
        var name=Parse(after);
        if(!name.IsExactIdentifier(after))throw Error("The new variable name must be one arithmetic identifier.");
        // Replace only reference leaves while rendering the same immutable AST.
        return Render(_root,_cancel,before,after);
    }
    public double Evaluate(Func<string,double> variable)
    {
        var pending=new Stack<(Node Node,bool Ready)>();var values=new Stack<double>();pending.Push((_root,false));
        while(pending.Count>0)
        {
            _cancel?.Invoke();var (node,ready)=pending.Pop();double value;
            if(node is Constant constant)value=constant.Value;
            else if(node is Reference reference)
            {value=variable(reference.Name);if(!double.IsFinite(value))throw Error("Referenced value is not finite.");}
            else if(!ready)
            {
                pending.Push((node,true));
                if(node is Unary pendingUnary)pending.Push((pendingUnary.Value,false));
                else if(node is Binary pendingBinary){pending.Push((pendingBinary.Right,false));pending.Push((pendingBinary.Left,false));}
                continue;
            }
            else if(node is Unary evaluatedUnary)value=evaluatedUnary.Operator=='-'?-values.Pop():values.Pop();
            else
            {
                var binary=(Binary)node;double right=values.Pop(),left=values.Pop();
                if(binary.Operator=='/'&&right==0)throw Error("Division by zero.");
                value=binary.Operator switch{'+'=>left+right,'-'=>left-right,'*'=>left*right,'/'=>left/right,_=>throw Error("Unknown operator.")};
            }
            if(!double.IsFinite(value))throw Error("Expression produced a non-finite value.");values.Push(value);
        }
        return values.Pop();
    }
    private static DomainException Error(string message)=>new("INVALID_MODEL_EXPRESSION",message);
    private static int Precedence(char operation)=>operation is '+' or '-'?1:2;
    private static string Render(Node root,Action? cancel,string? before=null,string? after=null)
    {
        var text=new System.Text.StringBuilder();long bytes=0;
        void Append(string value)
        {bytes+=Utf8.GetByteCount(value);if(bytes>StoredByteBudget)throw Error("Generated arithmetic exceeds the actual8MiB UTF8 syntax budget.");text.Append(value);}
        var pending=new Stack<(Node? Node,string? Text,int Parent,bool Right)>();pending.Push((root,null,0,false));
        while(pending.Count>0)
        {
            cancel?.Invoke();var part=pending.Pop();if(part.Text!=null){Append(part.Text);continue;}var node=part.Node!;
            // Unary plus is the existing canonical no-op; do not reassociate
            // binary operations, including addition/multiplication rounding.
            while(node is Unary{Operator:'+'} positive){cancel?.Invoke();node=positive.Value;}
            int precedence=node is Binary binary?Precedence(binary.Operator):node is Unary?3:4;
            bool wrap=precedence<part.Parent||part.Right&&node is Binary&&precedence==part.Parent;
            if(wrap){Append("(");pending.Push((null,")",0,false));}
            if(node is Constant constant)Append(constant.Value.ToString("R",CultureInfo.InvariantCulture));
            else if(node is Reference reference)Append(reference.Name==before?after!:reference.Name);
            else if(node is Unary unary){Append("-");pending.Push((unary.Value,null,precedence,false));}
            else
            {
                var operation=(Binary)node;
                pending.Push((operation.Right,null,precedence,true));pending.Push((null,operation.Operator.ToString(),0,false));
                pending.Push((operation.Left,null,precedence,false));
            }
        }
        return text.ToString();
    }
    // User policy remains separate from already observed/generated native syntax.
    public static ModelArithmetic Parse(string expression)=>ParseCore(expression,true,null);
    public static ModelArithmetic ParseStored(string expression,Action? cancel=null)=>ParseCore(expression,false,cancel);
    private static ModelArithmetic ParseCore(string expression,bool userInput,Action? cancel)
    {
        cancel?.Invoke();
        if(expression==null||expression.Length==0||expression.Length>(userInput?1024:StoredByteBudget)||userInput&&string.IsNullOrWhiteSpace(expression))
            throw Error(userInput?"Use1–1024 characters of arithmetic without control characters.":"Stored arithmetic exceeds the actual8MiB UTF8 syntax budget or is empty.");
        for(int i=0;i<expression.Length;i++){if((i&255)==0)cancel?.Invoke();if(char.IsControl(expression[i]))throw Error("Arithmetic cannot contain control characters.");}
        try{if(Utf8.GetByteCount(expression)>StoredByteBudget)throw Error("Stored arithmetic exceeds the actual8MiB UTF8 syntax budget.");}
        catch(System.Text.EncoderFallbackException){throw Error("Arithmetic must contain valid Unicode scalar text.");}
        return new(new Parser(expression,userInput,cancel).Parse(),cancel);
    }
    private sealed class Parser
    {
        private readonly string _text;private readonly bool _userInput;private readonly Action? _cancel;private int _position,_tokens,_nesting;
        private readonly Stack<(char Operator,bool Unary)> _operators=new();private readonly Stack<Node> _values=new();
        internal Parser(string text,bool userInput,Action? cancel){_text=text;_userInput=userInput;_cancel=cancel;}
        private void Check(){if((_position&255)==0)_cancel?.Invoke();}
        private void Token(){_cancel?.Invoke();if(++_tokens>128&&_userInput)throw Error("Expression exceeds128 tokens.");}
        private void Nest(){if(++_nesting>16&&_userInput)throw Error("Expression nesting exceeds16 levels.");}
        private void Reduce()
        {
            _cancel?.Invoke();var operation=_operators.Pop();
            if(operation.Unary){_values.Push(new Unary(operation.Operator,_values.Pop()));_nesting--;}
            else{var right=_values.Pop();var left=_values.Pop();_values.Push(new Binary(operation.Operator,left,right));}
        }
        private void ReduceUnary()
        {
            while(_operators.TryPeek(out var operation)&&operation.Unary)Reduce();
        }
        internal Node Parse()
        {
            bool operand=true;
            while(_position<_text.Length)
            {
                Check();char current=_text[_position];if(current==' '){_position++;continue;}
                if(operand)
                {
                    if(current is '+' or '-') {_position++;Token();Nest();_operators.Push((current,true));continue;}
                    if(current=='('){_position++;Token();Nest();_operators.Push(('(',false));continue;}
                    _values.Push(Value());ReduceUnary();operand=false;continue;
                }
                if(current==')')
                {
                    _position++;Token();while(_operators.TryPeek(out var operation)&&operation.Operator!='(')Reduce();
                    if(_operators.Count==0)throw Error("Unexpected closing parenthesis.");_operators.Pop();_nesting--;ReduceUnary();continue;
                }
                if(current is not ('+' or '-' or '*' or '/'))throw Error("Only numbers, existing variable names, parentheses and + - * / are supported.");
                _position++;Token();
                while(_operators.TryPeek(out var previous)&&previous.Operator!='('&&Precedence(previous.Operator)>=Precedence(current))Reduce();
                _operators.Push((current,false));operand=true;
            }
            if(operand)throw Error("Expression operand is missing.");
            while(_operators.Count>0){if(_operators.Peek().Operator=='(')throw Error("Closing parenthesis is missing.");Reduce();}
            return _values.Pop();
        }
        private Node Value()
        {
            int start=_position;char first=_text[_position];
            if(char.IsLetter(first)||first=='_')
            {
                _position++;while(_position<_text.Length&&(char.IsLetterOrDigit(_text[_position])||_text[_position]=='_')){Check();_position++;}
                string name=_text[start.._position];if(name.Length>64)throw Error("Variable name exceeds64 characters.");Token();return new Reference(name);
            }
            bool Digit(char c)=>c is >= '0' and <= '9';
            if(!Digit(first)&&first!='.')throw Error("Unexpected expression character.");
            while(_position<_text.Length&&Digit(_text[_position])){Check();_position++;}
            if(_position<_text.Length&&_text[_position]=='.'){_position++;while(_position<_text.Length&&Digit(_text[_position])){Check();_position++;}}
            if(_position<_text.Length&&_text[_position] is 'e' or 'E')
            {
                _position++;if(_position<_text.Length&&_text[_position] is '+' or '-')_position++;
                int exponent=_position;while(_position<_text.Length&&Digit(_text[_position])){Check();_position++;}if(exponent==_position)throw Error("Exponent digits are missing.");
            }
            if(!double.TryParse(_text[start.._position],NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||!double.IsFinite(value))throw Error("Invalid finite numeric literal.");
            Token();return new Constant(value);
        }
    }
}

public sealed record ModelExpressionVariable(string Id,string Name,string Expression,double? Value,string[] DownstreamVariables,bool IsDistance=true,bool IsAngleDegrees=false,bool IsPlacement=false,bool NativeOwnedControl=false);
public sealed record ModelExpressionPlan(string TargetId,string Expression,string[] AffectedIds,string[] ReferencedIds,IReadOnlyDictionary<string,double> ExpectedValues);

public static class ModelExpressionPlanning
{
    public static ModelExpressionPlan Prepare(ModelExpressionVariable[] variables,string targetId,string expression,bool propagate,Action? cancel=null,bool storedTargetExpression=false)
    {
        void Need(bool valid,string code,string message){if(!valid)throw new DomainException(code,message);}
        cancel?.Invoke();
        Need(variables.Length>=1,"VARIABLES_UNAVAILABLE","Use a current nonempty MCS variable inventory.");
        var byId=new Dictionary<string,ModelExpressionVariable>(StringComparer.OrdinalIgnoreCase);
        var byName=new Dictionary<string,List<ModelExpressionVariable>>(StringComparer.Ordinal);long graphBytes=2;
        foreach(var variable in variables)
        {
            cancel?.Invoke();graphBytes+=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(variable,Wire.Json).Length+1;
            Need(graphBytes<=8*1024*1024,"RESPONSE_LIMIT_EXCEEDED","Current variable graph exceeds the actual8MiB serialized snapshot budget.");
            Need(byId.TryAdd(variable.Id,variable),"VARIABLES_UNAVAILABLE","Variable IDs are not unique.");
            if(!byName.TryGetValue(variable.Name,out var names))byName.Add(variable.Name,names=new());names.Add(variable);
        }
        Need(byId.ContainsKey(targetId),"OBJECT_NOT_FOUND","Target variable is absent from the current inventory.");
        var affected=new HashSet<string>(StringComparer.OrdinalIgnoreCase){targetId};var queue=new Queue<string>();queue.Enqueue(targetId);
        while(queue.Count>0)
        {
            cancel?.Invoke();var node=byId[queue.Dequeue()];
            foreach(var child in node.DownstreamVariables)
            {
                cancel?.Invoke();Need(byId.ContainsKey(child),"VARIABLES_UNAVAILABLE","A dependent variable is missing from the current inventory.");
                Need(!child.Equals(targetId,StringComparison.OrdinalIgnoreCase),"CYCLIC_MODEL_EXPRESSION","Native downstream graph reaches the target again.");
                if(affected.Add(child))queue.Enqueue(child);
            }
        }
        Need(propagate||affected.Count==1,"PARAMETER_HAS_DEPENDENTS","Use propagate=true to intentionally update the related variables inside the selected body.");
        string Resolve(string name)
        {
            cancel?.Invoke();Need(byName.TryGetValue(name,out var matches)&&matches.Count==1,"VARIABLE_NAME_AMBIGUOUS","Expression name is absent or ambiguous; copy exact names from get_model_variables.");return matches![0].Id;
        }
        var target=storedTargetExpression?ModelArithmetic.ParseStored(expression,cancel):ModelArithmetic.Parse(expression);var references=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var name in target.References)
        {
            cancel?.Invoke();
            string id=Resolve(name);Need(!affected.Contains(id),"CYCLIC_MODEL_EXPRESSION","New expression depends on its target or an existing downstream variable.");references.Add(id);
        }
        var expressions=new Dictionary<string,ModelArithmetic>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in affected){cancel?.Invoke();expressions.Add(id,id.Equals(targetId,StringComparison.OrdinalIgnoreCase)?target:ModelArithmetic.ParseStored(byId[id].Expression,cancel));}
        // The number of dependencies is native model data, not recursion depth.
        // Schedule the affected DAG explicitly so long valid chains cannot exhaust
        // the CAD process stack after removing the old variable-count ceiling.
        var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        var waiting=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var consumers=affected.ToDictionary(id=>id,_=>new List<string>(),StringComparer.OrdinalIgnoreCase);
        var ready=new Queue<string>();
        foreach(string id in affected)
        {
            cancel?.Invoke();
            var dependencies=expressions[id].References.Select(Resolve).Where(affected.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            waiting.Add(id,dependencies.Length);
            foreach(string dependency in dependencies){cancel?.Invoke();consumers[dependency].Add(id);}
            if(dependencies.Length==0)ready.Enqueue(id);
        }
        double ReadValue(string id)
        {
            if(affected.Contains(id))
            {
                Need(values.ContainsKey(id),"CYCLIC_MODEL_EXPRESSION","Affected expressions form a cycle.");return values[id];
            }
            references.Add(id);Need(byId[id].Value is {} value&&double.IsFinite(value),"VARIABLE_VALUE_UNAVAILABLE","Referenced stored value is unavailable.");return byId[id].Value!.Value;
        }
        while(ready.Count>0)
        {
            cancel?.Invoke();string id=ready.Dequeue();
            double result=expressions[id].Evaluate(name=>ReadValue(Resolve(name)));
            var variable=byId[id];
            // A typed native feature owner validates its exact control domain
            // before writing. Count, offset and secondary controls are not all
            // positive distances or revolution angles merely because they have
            // a feature host. Keep the legacy abstract planner ranges for
            // callers that have not admitted such an owning control.
            Need(variable.NativeOwnedControl?double.IsFinite(result):variable.IsPlacement?Math.Abs(result)<=(variable.IsAngleDegrees?360:1e6):variable.IsAngleDegrees?result>0&&result<=360&&result*Math.PI/180>0:variable.IsDistance?result>=.001&&result<=1e6:Math.Abs(result)<=1e12,"INVALID_GEOMETRY","Use a finite value accepted by the exact native owning control. Legacy feature distances0.001–1000000 mm; positive revolution angles at most360 degrees; placement components +/-1000000 mm or +/-360 degrees; document User scalars within +/-1e12 native units.");
            values.Add(id,result);
            foreach(string child in consumers[id]){cancel?.Invoke();if(--waiting[child]==0)ready.Enqueue(child);}
        }
        Need(values.Count==affected.Count,"CYCLIC_MODEL_EXPRESSION","Affected expressions form a cycle.");
        return new(targetId,expression,affected.ToArray(),references.ToArray(),values);
    }
}
