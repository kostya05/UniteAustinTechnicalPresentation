using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

public class RenderingDataWrapper : SharedComponentDataProxy<RenderingData>
{
	
}

[Serializable]
public struct RenderingData : ISharedComponentData, IEquatable<RenderingData>
{
	public UnitType UnitType;
	public GameObject BakingPrefab;
	public Material Material;

	public LodData LodData;

	public bool Equals(RenderingData other)
	{
		return UnitType == other.UnitType;
	}

	public override bool Equals(object obj)
	{
		return obj is RenderingData other && Equals(other);
	}

	public override int GetHashCode()
	{
		return (int) UnitType;
	}
}
