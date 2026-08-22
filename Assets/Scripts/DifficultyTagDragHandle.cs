using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gugarhythm
{
    public sealed class DifficultyTagDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int Index;
        public Action<int, int> Moved;
        int startIndex;
        int lastTarget;
        Transform parent;

        public void OnBeginDrag(PointerEventData eventData) { parent = transform.parent; startIndex = Index; lastTarget = Index; transform.SetAsLastSibling(); }
        public void OnDrag(PointerEventData eventData)
        {
            if (parent == null) return;
            var parentRect = parent as RectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out var localPoint)) return;
            var halfRowHeight = 28f;
            var minY = parentRect.rect.yMin + halfRowHeight;
            var maxY = parentRect.rect.yMax - halfRowHeight;
            localPoint.y = Mathf.Clamp(localPoint.y, minY, maxY);
            var anchoredY = Mathf.Clamp(localPoint.y - parentRect.rect.yMax, -(parentRect.rect.height - halfRowHeight * 2f), -halfRowHeight);
            var selfRect = transform as RectTransform;
            selfRect.anchoredPosition = new Vector2(0, anchoredY);
            var target = Mathf.Clamp(Mathf.RoundToInt((-anchoredY - 28f) / 64f), 0, Mathf.Max(0, parent.childCount - 1));
            if (target == lastTarget) return;
            lastTarget = target;
            foreach (var sibling in parent.GetComponentsInChildren<DifficultyTagDragHandle>())
            {
                if (sibling == this) continue;
                var slot = sibling.Index;
                if (target > startIndex && slot > startIndex && slot <= target) slot--;
                else if (target < startIndex && slot >= target && slot < startIndex) slot++;
                StartCoroutine(AnimateTransform(sibling.transform as RectTransform, new Vector2(0, -slot * 64f - 28f), .12f));
            }
        }

        IEnumerator AnimateTransform(RectTransform target, Vector2 destination, float duration)
        {
            var origin = target.anchoredPosition;
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                target.anchoredPosition = Vector2.Lerp(origin, destination, elapsed / duration);
                yield return null;
            }
            target.anchoredPosition = destination;
        }
        public void OnEndDrag(PointerEventData eventData)
        {
            if (parent == null) return;
            var selfRect = transform as RectTransform;
            var target = Mathf.Clamp(Mathf.RoundToInt((-selfRect.anchoredPosition.y - 28f) / 64f), 0, Mathf.Max(0, parent.childCount - 1));
            var targetPosition = new Vector2(0, -target * 64f - 28f);
            StartCoroutine(AnimateDrop(targetPosition, startIndex, target));
        }

        IEnumerator AnimateDrop(Vector2 targetPosition, int from, int to)
        {
            var selfRect = transform as RectTransform;
            var startPosition = selfRect.anchoredPosition;
            const float duration = .18f;
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                selfRect.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, elapsed / duration);
                yield return null;
            }
            selfRect.anchoredPosition = targetPosition;
            Moved?.Invoke(from, to);
        }
    }
}
