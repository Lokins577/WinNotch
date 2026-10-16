using System;

namespace WinNotch.Helpers;

/// <summary>
/// Spring physics animation solver matching SwiftUI's interactiveSpring.
/// 
/// Equation: x(t) = target + e^(-ζωt) * (A·cos(ωd·t) + B·sin(ωd·t))
/// where ζ = damping ratio, ω = natural frequency, ωd = damped frequency.
/// 
/// SwiftUI's interactiveSpring(response, dampingFraction):
///   ω = 2π / response
///   ζ = dampingFraction
/// </summary>
public class SpringAnimator
{
    private double _omega;       // Natural frequency (ω = 2π / response)
    private double _zeta;        // Damping ratio (ζ)
    private double _omegaD;      // Damped frequency

    private double _startValue;
    private double _targetValue;
    private double _startVelocity;
    private DateTime _startTime;
    private bool _isAnimating;

    public double Response { get; set; } = 0.40;
    public double DampingFraction { get; set; } = 0.82;

    public bool IsAnimating => _isAnimating;
    public double CurrentValue { get; private set; }

    public event Action<double>? ValueChanged;
    public event Action? Completed;

    public SpringAnimator() { }

    public SpringAnimator(double response, double dampingFraction)
    {
        Response = response;
        DampingFraction = dampingFraction;
    }

    public void AnimateTo(double target, double currentValue, double velocity = 0)
    {
        _startValue = currentValue;
        _targetValue = target;
        _startVelocity = velocity;
        _startTime = DateTime.UtcNow;

        _omega = 2.0 * Math.PI / Response;
        _zeta = DampingFraction;
        _omegaD = _omega * Math.Sqrt(Math.Max(1.0 - _zeta * _zeta, 0.001));

        _isAnimating = true;
        CurrentValue = currentValue;
    }

    /// <summary>
    /// Call this every frame (e.g., from CompositionTarget.Rendering).
    /// Returns current interpolated value.
    /// </summary>
    public double Tick()
    {
        if (!_isAnimating)
            return CurrentValue;

        double t = (DateTime.UtcNow - _startTime).TotalSeconds;

        double displacement = _startValue - _targetValue;
        double value;

        if (_zeta >= 1.0)
        {
            // Critically or over-damped
            double expTerm = Math.Exp(-_omega * _zeta * t);
            double A = displacement;
            double B = _startVelocity + _omega * _zeta * displacement;
            value = _targetValue + expTerm * (A + B * t);
        }
        else
        {
            // Under-damped (bouncy)
            double expTerm = Math.Exp(-_zeta * _omega * t);
            double A = displacement;
            double B = (_startVelocity + _zeta * _omega * displacement) / _omegaD;
            value = _targetValue + expTerm * (A * Math.Cos(_omegaD * t) + B * Math.Sin(_omegaD * t));
        }

        CurrentValue = value;
        ValueChanged?.Invoke(value);

        // Check if settled (close enough to target with minimal velocity)
        if (Math.Abs(value - _targetValue) < 0.3 && t > 0.05)
        {
            double dt = 0.001;
            double t2 = t + dt;
            double nextValue;
            if (_zeta >= 1.0)
            {
                double exp2 = Math.Exp(-_omega * _zeta * t2);
                double A = displacement;
                double B = _startVelocity + _omega * _zeta * displacement;
                nextValue = _targetValue + exp2 * (A + B * t2);
            }
            else
            {
                double exp2 = Math.Exp(-_zeta * _omega * t2);
                double A = displacement;
                double B = (_startVelocity + _zeta * _omega * displacement) / _omegaD;
                nextValue = _targetValue + exp2 * (A * Math.Cos(_omegaD * t2) + B * Math.Sin(_omegaD * t2));
            }

            double approxVelocity = Math.Abs((nextValue - value) / dt);
            if (approxVelocity < 0.5)
            {
                CurrentValue = _targetValue;
                _isAnimating = false;
                ValueChanged?.Invoke(_targetValue);
                Completed?.Invoke();
            }
        }

        return CurrentValue;
    }
}
