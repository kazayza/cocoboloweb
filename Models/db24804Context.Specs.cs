using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

public partial class db24804Context
{
    public virtual DbSet<SpecList> SpecLists { get; set; }
    public virtual DbSet<SpecOption> SpecOptions { get; set; }
    public virtual DbSet<SpecQuestion> SpecQuestions { get; set; }
    public virtual DbSet<SpecAnswer> SpecAnswers { get; set; }
}
