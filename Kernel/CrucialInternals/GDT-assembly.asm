; MASM 64-bit syntax
.code

; RCX holds the pointer to GDTDescriptor
public LoadGDT

LoadGDT proc
    lgdt fword ptr [rcx]
    
    ; Setup Data Segments (0x10)
    mov ax, 10h 
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    
    ; Setup Code Segment (0x08) using a far return
    pop rdi
    mov rax, 08h 
    push rax
    push rdi
    retfq
LoadGDT endp

end