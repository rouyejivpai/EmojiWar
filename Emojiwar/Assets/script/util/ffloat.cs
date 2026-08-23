
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ffloat
{
    [SerializeField] private float _baseValue;
    
    public float add;
    public float mul;
    
    public ffloat(float value = 0f)
    {
        _baseValue = value;
    }
    
    public float BaseValue
    {
        get => _baseValue;
        set => _baseValue = value;
    }
    
    public float CurrentValue
    {
        get
        {
            return (_baseValue + add) * (1+mul);
        }
    }
    
    // 只保留最常用的运算符
    public static ffloat operator +(ffloat left, float right) => new ffloat(left.CurrentValue + right);
    public static ffloat operator +(float left, ffloat right) => new ffloat(left + right.CurrentValue);
    public static ffloat operator *(ffloat left, float right) => new ffloat(left.CurrentValue * right);
    public static ffloat operator *(float left, ffloat right) => new ffloat(left * right.CurrentValue);
    
    // 隐式转换
    public static implicit operator ffloat(float value) => new ffloat(value);
    public static implicit operator float(ffloat value) => value.CurrentValue;
    
    // Buff管理方法
    
    
    public override string ToString() => $"ffloat({CurrentValue})";
}