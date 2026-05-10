using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class BiometricLog
{
    public int BiometricLogId { get; set; }

    public int BiometricCode { get; set; }

    public DateTime LogDate { get; set; }

    public TimeOnly LogTime { get; set; }
}
