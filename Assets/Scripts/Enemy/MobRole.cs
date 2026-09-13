// Which escort role (if any) a mob plays in the Skeleton Tactician
// encounter - see EnemyAI's role-specific fields/methods and
// BOSS_DESIGN.md. None (the default) is every other mob in the game
// (Goblin, Ogre, ...), completely unaffected by any of this.
public enum MobRole
{
    None,
    SkeletonWarrior,
    SkeletonArcher,
    SkeletonHealer,
    SkeletonMage,
    SkeletonTactician,
}
