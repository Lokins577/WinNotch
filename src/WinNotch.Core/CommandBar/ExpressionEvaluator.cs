using System.Globalization;

namespace WinNotch.CommandBar;

public static class ExpressionEvaluator
{
    public static bool TryEvaluate(string? expression, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 256) return false;
        try
        {
            var parser = new Parser(expression);
            result = parser.Parse();
            return double.IsFinite(result);
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;

        public Parser(string text) => _text = text;

        public double Parse()
        {
            double value = ParseExpression();
            SkipWhitespace();
            if (_position != _text.Length) throw new FormatException();
            return value;
        }

        private double ParseExpression()
        {
            double value = ParseTerm();
            while (true)
            {
                if (Match('+')) value += ParseTerm();
                else if (Match('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            double value = ParsePower();
            while (true)
            {
                if (Match('*')) value *= ParsePower();
                else if (Match('/'))
                {
                    double divisor = ParsePower();
                    if (divisor == 0) throw new DivideByZeroException();
                    value /= divisor;
                }
                else if (Match('%'))
                {
                    double divisor = ParsePower();
                    if (divisor == 0) throw new DivideByZeroException();
                    value %= divisor;
                }
                else return value;
            }
        }

        private double ParsePower()
        {
            double value = ParseUnary();
            return Match('^') ? Math.Pow(value, ParsePower()) : value;
        }

        private double ParseUnary()
        {
            if (Match('+')) return ParseUnary();
            if (Match('-')) return -ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            if (Match('('))
            {
                double value = ParseExpression();
                if (!Match(')')) throw new FormatException();
                return value;
            }

            SkipWhitespace();
            if (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
                return ParseNumber();

            string name = ParseIdentifier().ToLowerInvariant();
            if (name == "pi") return Math.PI;
            if (name == "e") return Math.E;
            if (!Match('(')) throw new FormatException();
            double argument = ParseExpression();
            if (!Match(')')) throw new FormatException();
            return name switch
            {
                "sqrt" => Math.Sqrt(argument),
                "abs" => Math.Abs(argument),
                "sin" => Math.Sin(argument),
                "cos" => Math.Cos(argument),
                "tan" => Math.Tan(argument),
                "asin" => Math.Asin(argument),
                "acos" => Math.Acos(argument),
                "atan" => Math.Atan(argument),
                "ln" => Math.Log(argument),
                "log" => Math.Log10(argument),
                "exp" => Math.Exp(argument),
                "floor" => Math.Floor(argument),
                "ceil" => Math.Ceiling(argument),
                "round" => Math.Round(argument),
                _ => throw new FormatException()
            };
        }

        private double ParseNumber()
        {
            int start = _position;
            while (_position < _text.Length
                   && (char.IsDigit(_text[_position]) || _text[_position] is '.' or 'e' or 'E'
                       || (_text[_position] is '+' or '-'
                           && _position > start && _text[_position - 1] is 'e' or 'E')))
                _position++;
            return double.Parse(_text[start.._position], CultureInfo.InvariantCulture);
        }

        private string ParseIdentifier()
        {
            SkipWhitespace();
            int start = _position;
            while (_position < _text.Length && char.IsLetter(_text[_position])) _position++;
            if (start == _position) throw new FormatException();
            return _text[start.._position];
        }

        private bool Match(char character)
        {
            SkipWhitespace();
            if (_position >= _text.Length || _text[_position] != character) return false;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
        }
    }
}
