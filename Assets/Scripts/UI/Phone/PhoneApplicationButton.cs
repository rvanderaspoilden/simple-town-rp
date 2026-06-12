using DG.Tweening;
using Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PhoneApplicationButton : MonoBehaviour, IPointerClickHandler, IPointerExitHandler, IPointerEnterHandler {
    [SerializeField]
    private AudioClip hoverSound;

    [SerializeField]
    private AudioClip clickSound;

    [SerializeField]
    private PhoneApplicationUI application;

    [Tooltip("Si coché : au clic, ouvre la constellation et referme le téléphone (au lieu " +
             "d'ouvrir une PhoneApplicationUI). Laisser 'application' vide dans ce cas, et NE PAS " +
             "ajouter ce bouton à la liste 'applications' du PhoneControllerUI.")]
    [SerializeField]
    private bool opensConstellation;

    [SerializeField]
    private Image icon;

    public PhoneApplicationUI Application => application;

    public void OnPointerClick(PointerEventData eventData) {
        HUDManager.Instance.PlaySound(clickSound, 1f);

        if (this.opensConstellation) {
            // Ouvre la constellation et LAISSE le téléphone ouvert derrière. Le téléphone
            // se refermera quand le joueur fermera la constellation (ConstellationUI.Close).
            HUDManager.Instance.ConstellationUI?.Open();
            return;
        }

        PhoneControllerUI.Instance.OpenApplication(this);
    }

    public void OnPointerEnter(PointerEventData eventData) {
        this.icon.transform.DOComplete();
        this.icon.transform.DOScale(new Vector3(1.1f, 1.1f, 1f), .3f);
        HUDManager.Instance.PlaySound(hoverSound, 1f);
    }

    public void OnPointerExit(PointerEventData eventData) {
        this.icon.transform.DOComplete();
        this.icon.transform.DOScale(new Vector3(1f, 1f, 1f), .3f);
    }
}
