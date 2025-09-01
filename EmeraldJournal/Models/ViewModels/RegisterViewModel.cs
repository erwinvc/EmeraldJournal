﻿using System.ComponentModel.DataAnnotations;

namespace EmeraldJournal.Models.ViewModels;

public class RegisterViewModel {
    [Required] public string UserName { get; set; } = "";
    [Required] public string Password { get; set; } = "";
    public string Role { get; set; } = "User"; 
}