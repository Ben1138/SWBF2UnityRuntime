using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LibSWBF2;

public abstract class ISWBFGameClass : MonoBehaviour
{
    public abstract void Init(LibSWBF2.Wrappers.Instance inst);
}