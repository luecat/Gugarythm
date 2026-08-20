using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gugarythm
{
    public sealed class DifficultyTagDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int Index;
        public Action<int, int> Moved;
        int startIndex;
        Transform parent;

        public void OnBeginDrag(PointerEventData eventData) { parent = transform.parent; startIndex = Index; transform.SetAsLastSibling(); }
        public void OnDrag(PointerEventData eventData) { transform.position = eventData.position; }
        public void OnEndDrag(PointerEventData eventData)
        {
            if (parent == null) return;
            var target = Mathf.Clamp(Mathf.RoundToInt((-transform.localPosition.y) / 48f), 0, Mathf.Max(0, parent.childCount - 1));
            Moved?.Invoke(startIndex, target);
        }
    }
}
