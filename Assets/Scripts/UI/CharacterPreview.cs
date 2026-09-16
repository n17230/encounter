using UnityEngine;

// Local-only, non-networked live preview of the character currently being
// built in the Character Creation menu panel. Deliberately a separate rig
// instance from the live networked player (CharacterAppearance) rather than
// a reuse of it - this menu is used both pregame, before any NetworkObject
// exists at all, and from the in-game Escape menu, and a single
// implementation that doesn't depend on a network session covers both
// without repositioning/hijacking the real in-world character while the
// menu is open. The rig sits far from the playable area (see the Editor
// setup notes) and is captured by its own Camera into a RenderTexture that
// MainMenu displays.
public class CharacterPreview : MonoBehaviour
{
    [SerializeField] private Transform maleRigRoot;
    [SerializeField] private Transform femaleRigRoot;

    private CharacterAppearanceApplier applier;
    // Shared between genders (not reset on switch) so rotating to a
    // preferred viewing angle survives a Male/Female toggle.
    private float rotationY;

    private void Awake()
    {
        applier = new CharacterAppearanceApplier(maleRigRoot, femaleRigRoot);
    }

    // Called every frame the Character Creation panel is drawn, so the
    // preview updates the instant a choice changes - no networking and no
    // change-detection needed here, this is cheap enough to just re-run.
    public void Refresh(PlayerProfile profile)
    {
        applier.Apply(new AppearanceSelection
        {
            Female = profile.AppearanceIsFemale,
            TopId = profile.AppearanceTopId,
            BottomId = profile.AppearanceBottomId,
            HeadwearId = profile.AppearanceHeadwearId,
            EyebrowsId = profile.AppearanceEyebrowsId,
            EyesId = profile.AppearanceEyesId,
            MouthId = profile.AppearanceMouthId,
            HairId = profile.AppearanceHairId,
            FacialHairId = profile.AppearanceFacialHairId,
            AccessoryIds = profile.AppearanceAccessoryIds,
            BodyColorIndex = profile.AppearanceBodyColorIndex,
            ObjectColorIndex = profile.AppearanceObjectColorIndex,
        });

        Transform activeRig = profile.AppearanceIsFemale ? femaleRigRoot : maleRigRoot;
        if (activeRig != null) activeRig.localRotation = Quaternion.Euler(0f, rotationY, 0f);
    }

    // Positive turns right, negative turns left - see MainMenu's rotate
    // buttons on the Character Creation panel.
    public void Rotate(float degrees)
    {
        rotationY += degrees;
    }

    private void OnDestroy()
    {
        applier?.Cleanup();
    }
}
