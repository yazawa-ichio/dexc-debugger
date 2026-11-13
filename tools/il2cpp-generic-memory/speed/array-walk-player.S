// The inner loops of SpeedBench.IndexArray exactly as the Android Player has them (disassembly of e-v2-os and
// e-v2-o3), 16 passes each. x0 = the array, returns the sum in w0.
	.text
	.p2align 4
	.global player_os
	.type player_os, %function
player_os:
	mov	x13, x0
	ldr	w8, [x13, #0x18]
	mov	w0, wzr
	mov	w10, wzr
	bic	w9, w8, w8, asr #31
1:	cmp	w8, #0x1
	b.lt	3f
	mov	w11, wzr
2:	cmp	w8, w11
	b.eq	9f
	add	x12, x13, w11, sxtw #3
	ldr	x12, [x12, #0x20]
	cbz	x12, 9f
	ldr	w12, [x12, #0x10]
	add	w11, w11, #0x1
	cmp	w9, w11
	add	w0, w12, w0
	b.ne	2b
3:	add	w10, w10, #0x1
	cmp	w10, #0x10
	b.ne	1b
	ret
9:	brk	#0x1

	.p2align 4
	.global player_o3
	.type player_o3, %function
player_o3:
	mov	x13, x0
	ldr	x9, [x13, #0x18]
	mov	w0, wzr
	and	x8, x9, #0xffffffff
	.rept 16
	add	x9, x13, #0x20
	mov	x10, x8
1:	ldr	x11, [x9]
	cbz	x11, 9f
	ldr	w11, [x11, #0x10]
	subs	x10, x10, #0x1
	add	x9, x9, #0x8
	add	w0, w11, w0
	b.ne	1b
	.endr
	ret
9:	brk	#0x1

// The loop of -O3 once, inside a loop of 16 passes.
	.p2align 4
	.global player_o3single
	.type player_o3single, %function
player_o3single:
	mov	x13, x0
	ldr	x9, [x13, #0x18]
	mov	w0, wzr
	mov	w12, wzr
	and	x8, x9, #0xffffffff
2:	add	x9, x13, #0x20
	mov	x10, x8
1:	ldr	x11, [x9]
	cbz	x11, 9f
	ldr	w11, [x11, #0x10]
	subs	x10, x10, #0x1
	add	x9, x9, #0x8
	add	w0, w11, w0
	b.ne	1b
	add	w12, w12, #0x1
	cmp	w12, #0x10
	b.ne	2b
	ret
9:	brk	#0x1
