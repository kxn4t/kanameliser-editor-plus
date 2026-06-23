using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// コンポーネント情報を格納するクラス
    /// </summary>
    public class ComponentInfo
    {
        public GameObject GameObject;
        public Component Component;
        public bool IsSelected;

        public string Name
        {
            get
            {
                return Component != null ? Component.GetType().Name : "Missing Component";
            }
        }
    }

}
