namespace Marraia.Postgres.Uow.Interfaces
{
    public interface IUnitOfWork
    {
        UnitOfWork BeginTransaction();
    }
}
