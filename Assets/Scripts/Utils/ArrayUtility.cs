using System;
using System.Collections;
using System.Collections.Generic;

public static class ArrayUtility
  {
    public static void Add<T>(ref T[] array, T item)
    {
      Array.Resize<T>(ref array, array.Length + 1);
      array[array.Length - 1] = item;
    }

    public static bool ArrayEquals<T>(T[] lhs, T[] rhs)
    {
      if (lhs == null || rhs == null)
        return lhs == rhs;
      if (lhs.Length != rhs.Length)
        return false;
      for (int index = 0; index < lhs.Length; ++index)
      {
        if (!lhs[index].Equals((object) rhs[index]))
          return false;
      }
      return true;
    }

    public static bool ArrayReferenceEquals<T>(T[] lhs, T[] rhs)
    {
      if (lhs == null || rhs == null)
        return lhs == rhs;
      if (lhs.Length != rhs.Length)
        return false;
      for (int index = 0; index < lhs.Length; ++index)
      {
        if ((object) lhs[index] != (object) rhs[index])
          return false;
      }
      return true;
    }

    public static void AddRange<T>(ref T[] array, T[] items)
    {
      int length = array.Length;
      Array.Resize<T>(ref array, array.Length + items.Length);
      for (int index = 0; index < items.Length; ++index)
        array[length + index] = items[index];
    }

    public static void Insert<T>(ref T[] array, int index, T item)
    {
      ArrayList arrayList = new ArrayList();
      arrayList.AddRange((ICollection) array);
      arrayList.Insert(index, (object) item);
      array = arrayList.ToArray(typeof (T)) as T[];
    }

    public static void Remove<T>(ref T[] array, T item)
    {
      List<T> objList = new List<T>((IEnumerable<T>) array);
      objList.Remove(item);
      array = objList.ToArray();
    }

    public static List<T> FindAll<T>(T[] array, Predicate<T> match) => new List<T>((IEnumerable<T>) array).FindAll(match);

    public static T Find<T>(T[] array, Predicate<T> match) => new List<T>((IEnumerable<T>) array).Find(match);

    public static int FindIndex<T>(T[] array, Predicate<T> match) => new List<T>((IEnumerable<T>) array).FindIndex(match);

    public static int IndexOf<T>(T[] array, T value) => new List<T>((IEnumerable<T>) array).IndexOf(value);

    public static int LastIndexOf<T>(T[] array, T value) => new List<T>((IEnumerable<T>) array).LastIndexOf(value);

    public static void RemoveAt<T>(ref T[] array, int index)
    {
      List<T> objList = new List<T>((IEnumerable<T>) array);
      objList.RemoveAt(index);
      array = objList.ToArray();
    }

    public static bool Contains<T>(T[] array, T item) => new List<T>((IEnumerable<T>) array).Contains(item);

    public static void Clear<T>(ref T[] array)
    {
      Array.Clear((Array) array, 0, array.Length);
      Array.Resize<T>(ref array, 0);
    }
  }