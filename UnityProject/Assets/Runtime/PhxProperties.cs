using System;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using UnityEngine;
using LibSWBF2.Wrappers;
using LibSWBF2.Enums;
using JetBrains.Annotations;
using System.Collections;

public interface PhxPropRef
{
    void SetFromString(string val);
    void Set(object val);
}

/// <summary>
/// Encapsulates a value to be passable as reference.
/// </summary>
public sealed class PhxProp<T> : PhxPropRef
{
    public Action<T> OnValueChanged;
    T Value;

    public PhxProp(T val)
    {
        Value = val;
    }

    public T Get()
    {
        return Value;
    }

    public void SetFromString(string val)
    {
        T oldValue = Value;
        Value = PhxPropertyDB.FromString<T>(val);
        OnValueChanged?.Invoke(oldValue);
    }

    public void Set(object val)
    {
        T oldValue = Value;
        if (val.GetType() == typeof(string))
        {
            SetFromString(val as string);
            return;
        }

        try
        {
            Value = (T)val;
        }
        catch
        {
            Value = (T)Convert.ChangeType(val, typeof(T), CultureInfo.InvariantCulture);
        }
        OnValueChanged?.Invoke(oldValue);
    }

    public void Set(T val)
    {
        T oldValue = Value;
        Value = val;
        OnValueChanged?.Invoke(oldValue);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator T(PhxProp<T> refVal)
    {
        return refVal.Value;
    }
}

/// <summary>
/// Encapsulates multiple values of the same property name to be passable as reference.<br/>
/// Example from com_bldg_controlzone:<br/>
///     AmbientSound = "all com_blg_commandpost_goodie defer"<br/>
///     AmbientSound = "cis com_blg_commandpost_baddie defer"<br/>
///     AmbientSound = "imp com_blg_commandpost_baddie defer"<br/>
///     AmbientSound = "rep com_blg_commandpost_goodie defer"
/// </summary>
public sealed class PhxMultiProp : PhxPropRef
{
    public List<object[]> Values { get; private set; } = new List<object[]>();
    Type[] ExpectedTypes;


    public PhxMultiProp(params Type[] expectedTypes)
    {
        ExpectedTypes = expectedTypes;
    }

    public T Get<T>(int argIdx)
    {
        return Values.Count > 0 ? (T)Values[0][argIdx] : default;
    }

    // "Set" here actually adds another prop entry
    public void SetFromString(string val)
    {
        object[] vals = new object[ExpectedTypes.Length];
        List<string> split = new List<string>(val.Split(' '));
        split.RemoveAll(str => string.IsNullOrEmpty(str));
        if (split.Count > ExpectedTypes.Length)
        {
            Debug.LogWarning($"Encountered more property args ({split.Count}) than expected ({ExpectedTypes.Length})! Ignoring surplus...");
        }
        for (int i = 0; i < vals.Length; ++i)
        {
            split[i] = split[i].Trim();
            try
            {
                vals[i] = PhxPropertyDB.FromString(ExpectedTypes[i], split[i]);
            }
            catch
            {
                Debug.LogError($"Property arg value '{split[i]}' does not match expected arg type '{ExpectedTypes[i]}'");
            }
        }
        Values.Add(vals);
    }

    // "Set" here actually adds another prop entry
    public void Set(object val)
    {
        if (val.GetType() != typeof(string))
        {
            Debug.LogError("MultiProp's can only be added from strings!");
            return;
        }
        SetFromString(val as string);
    }
}