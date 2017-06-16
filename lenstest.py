import math
import itertools

refractionAir = 1.000293

def MatrixDimensions(m):
	nRows = len(m)
	nCols = len(m[0])
	for row in m:
		if len(row) != nCols:
			raise "All rows must have same column number"
	return nRows, nCols


def MatMul(a, b):
    rowsA, colsA = MatrixDimensions(a)
    rowsB, colsB = MatrixDimensions(b)

    if not colsA == rowsB:
        raise "Mismatch in matrix dimensions"

    result = [[0. for _ in range(colsB)] for __ in  range(rowsA)]
    
    for i in range(rowsA):
        for j in range(colsB):
            sourceRow = a[i]
            sourceCol = [b[x][j] for x in range(colsA)]
            result[i][j] +=  sum(sourceRow[y] * sourceCol[y] for y in range(colsA))

    return result


def TranslationMatrix(d):
    return [[1, d], [0, 1]]


def RefractionMatrixCurved(n1, n2, r):
    return [[1, 0], [(n1 - n2) / (r * n2), n1 / n2]]


def RefractionMatrixFlat(n1, n2):
    return [[1, 0], [0, n1 / n2]]


def ReflectionMatrixCurved(r):
    return [[1, 0], [2. / r, 1]]


def ReflectionMatrixFlat():
    return [[1, 0], [0, 1]]


def DMatrix(n1, n2, r, d):
    if not r == 0:
        return MatMul(TranslationMatrix(d), RefractionMatrixCurved(n1, n2, r))
    else:
        return MatMul(TranslationMatrix(d), RefractionMatrixFlat(n1, n2))

def main():
    T0 = TranslationMatrix(10)
    D1 = DMatrix(refractionAir, 1.652, 30, 7.7)
    D2 = DMatrix(1.652, 1.602, -89.350, 1.850)
    D3 = DMatrix(1.602, refractionAir, 580.380, 3.520)
    L4 = ReflectionMatrixCurved(-80.630)
    T3 = TranslationMatrix(3.520)
    R3inv = RefractionMatrixCurved(refractionAir, 1.602, -580.380)
    T2 = TranslationMatrix(1.850)
    L2inv = ReflectionMatrixCurved(89.350)
    D4 = DMatrix(refractionAir, 1.643, -80.630, 1.850)
    D5 = DMatrix(1.643, refractionAir, 28.340, 4.180)

    T6 = TranslationMatrix(3)
    D7 = DMatrix(refractionAir, 1.581, 0, 1.850)
    D8 = DMatrix(1.581, 1.694, 32.190, 7.270)
    D9 = DMatrix(1.694, refractionAir, -52.990, 81.857)

    Ms = MatMul(D7, T6)
    Ms = MatMul(D8, Ms)
    Ms = MatMul(D9, Ms)

    Ma = MatMul(D1, T0)
    Ma = MatMul(D2, Ma)
    Ma = MatMul(D3, Ma)
    Ma = MatMul(L4, Ma)
    Ma = MatMul(T3, Ma)
    Ma = MatMul(R3inv, Ma)
    Ma = MatMul(T2, Ma)
    Ma = MatMul(L2inv, Ma)
    Ma = MatMul(T2, Ma)
    Ma = MatMul(D3, Ma)
    Ma = MatMul(D4, Ma)
    Ma = MatMul(D5, Ma)

    v = [[-1], [0]]

    print Ma
    print Ms



if __name__ == "__main__":
    main()
